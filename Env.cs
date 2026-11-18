using OsbKT;

namespace Osb.Shell.Userspace
{
    // Slice 3 environment helpers: $VAR expansion for the command line and the
    // $PROMPT renderer. All values come from the kernel through 63h GETENV
    // (per-session store; see osbkt/docs/ABI.md v5). No GC allocations: every
    // buffer is a stackalloc.
    //
    // Expansion rules (legacy parity, Osb.Kernel.Shell.Environment.Expand):
    //   * $NAME (letters/digits/underscore) is replaced by its value;
    //   * an undefined name round-trips literally, so SET PROMPT=$P$G keeps its
    //     prompt tokens (they are not environment variables);
    //   * a lone '$' at the end stays '$'.
    internal static unsafe class Env
    {
        public const int ExpandCap = 256;   // line expansion buffer
        public const int PromptCap = 296;   // cwd + name + fixed tokens
        public const int ValueCap = 256;    // one variable's value

        // Reads NAME (char*, not NUL-terminated) into buf. Returns the byte
        // length (0 for a defined-but-empty value), -4 when undefined, or
        // another negative error from the kernel.
        public static int Get(char* name, int nameLen, byte* buf, int cap)
        {
            if (nameLen <= 0) return 0;
            if (nameLen > 63) nameLen = 63;
            var nbuf = stackalloc byte[64];
            for (int k = 0; k < nameLen; k++) nbuf[k] = (byte)name[k];
            return Syscall.GetEnv(nbuf, nameLen, buf, cap);
        }

        public static int Get(char* name, int nameLen, char* dst, int cap)
        {
            var vbuf = stackalloc byte[ValueCap];
            int n = Get(name, nameLen, vbuf, ValueCap);
            if (n <= 0) return n;
            if (n > cap - 1) n = cap - 1;
            for (int k = 0; k < n; k++) dst[k] = (char)vbuf[k];
            return n;
        }

        // Expands $VAR references in src into dst; returns the new length.
        public static int Expand(char* src, int slen, char* dst, int dcap)
        {
            int o = 0;
            int i = 0;
            while (i < slen && o < dcap - 1)
            {
                if (src[i] != '$') { dst[o++] = src[i++]; continue; }
                if (i + 1 >= slen) { dst[o++] = '$'; i++; continue; }
                char n0 = src[i + 1];
                bool ident = (n0 >= 'A' && n0 <= 'Z') || (n0 >= 'a' && n0 <= 'z') || n0 == '_';
                if (!ident) { dst[o++] = '$'; i++; continue; }
                int st = i + 1;
                int j = st;
                while (j < slen && (((src[j] >= 'A' && src[j] <= 'Z') || (src[j] >= 'a' && src[j] <= 'z') ||
                                     (src[j] >= '0' && src[j] <= '9') || src[j] == '_'))) j++;
                int nameLen = j - st;
                char* nm = stackalloc char[64];
                for (int k = 0; k < nameLen && k < 63; k++) nm[k] = src[st + k];
                var vbuf = stackalloc byte[ValueCap];
                int vl = Get(nm, nameLen, vbuf, ValueCap);
                if (vl >= 0)
                {
                    for (int k = 0; k < vl && o < dcap - 1; k++) dst[o++] = (char)vbuf[k];
                    i = j;
                    continue;
                }
                dst[o++] = '$';
                i++;
            }
            dst[o] = '\0';
            return o;
        }

        // Renders the prompt from $PROMPT (default "$P$G") into dst.
        // Tokens: $P path  $N drive  $G >  $L <  $B |  $Q =  $S space
        //         $V version  $_ newline  $$ literal $, or any $VAR.
        // $D/$T (date/time) need the DATETIME syscall (72h, not in v5) and are
        // left literal for now.
        public static int RenderPrompt(char* dst, int dcap)
        {
            var tmpl = stackalloc char[128];
            int tlen;
            fixed (char* pr = "PROMPT") tlen = Get(pr, 6, tmpl, 128);
            if (tlen < 0)
            {
                fixed (char* p = "$P$G") { for (int k = 0; k < 4; k++) tmpl[k] = p[k]; }
                tlen = 4;
            }
            if (tlen > 127) tlen = 127;

            var cwd = stackalloc byte[256];
            int clen = Syscall.GetCwd(cwd, 256);
            if (clen < 0) clen = 0;

            int o = 0;
            for (int k = 0; k < tlen; k++)
            {
                if (tmpl[k] != '$') { if (o < dcap - 1) dst[o++] = tmpl[k]; continue; }
                if (k + 1 >= tlen) { if (o < dcap - 1) dst[o++] = '$'; continue; }
                char t = Commands.Upper(tmpl[k + 1]);
                switch (t)
                {
                    case 'P': for (int q = 0; q < clen && o < dcap - 1; q++) dst[o++] = (char)cwd[q]; k++; continue;
                    case 'N': if (o < dcap - 1) dst[o++] = clen > 0 ? (char)cwd[0] : 'A'; k++; continue;
                    case 'G': if (o < dcap - 1) dst[o++] = '>'; k++; continue;
                    case 'L': if (o < dcap - 1) dst[o++] = '<'; k++; continue;
                    case 'B': if (o < dcap - 1) dst[o++] = '|'; k++; continue;
                    case 'Q': if (o < dcap - 1) dst[o++] = '='; k++; continue;
                    case 'S': if (o < dcap - 1) dst[o++] = ' '; k++; continue;
                    case 'V': fixed (char* v = "OSB 0.1") { for (int q = 0; q < 7 && o < dcap - 1; q++) dst[o++] = v[q]; } k++; continue;
                    case '$': if (o < dcap - 1) dst[o++] = '$'; k++; continue;
                    case '_': if (o < dcap - 1) dst[o++] = '\n'; k++; continue;
                }
                int st = k + 1;
                int j = st;
                while (j < tlen && (((tmpl[j] >= 'A' && tmpl[j] <= 'Z') || (tmpl[j] >= 'a' && tmpl[j] <= 'z') ||
                                     (tmpl[j] >= '0' && tmpl[j] <= '9') || tmpl[j] == '_'))) j++;
                if (j > st)
                {
                    int nlen = j - st;
                    char* nm = stackalloc char[64];
                    for (int q = 0; q < nlen && q < 63; q++) nm[q] = tmpl[st + q];
                    var vbuf = stackalloc byte[ValueCap];
                    int vl = Get(nm, nlen, vbuf, ValueCap);
                    if (vl >= 0)
                    {
                        for (int q = 0; q < vl && o < dcap - 1; q++) dst[o++] = (char)vbuf[q];
                        k = j - 1;
                        continue;
                    }
                    if (o < dcap - 1) dst[o++] = '$';
                    for (int q = st; q < j && o < dcap - 1; q++) dst[o++] = tmpl[q];
                    k = j - 1;
                    continue;
                }
                if (o < dcap - 1) dst[o++] = '$';
            }
            return o;
        }
    }
}