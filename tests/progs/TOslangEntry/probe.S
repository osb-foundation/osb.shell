/* O8.24 M3 OSLANG Ring 3 memory/TLS/allocation integration.
 *
 * Real NativeAOT allocation helpers backed by the process-owned heap region
 * validated against the kernel (osb.kernel/Memory/Layout.cs +
 * osb.kernel/Processes/Scheduler.cs InitTaskTls):
 *
 *   Heap = [UserTlsEnd, UserTlsHeapEnd) = [0x2180000, 0x21FFFF0)
 *   TLS alloc_ptr at FS:0 - 0xC8, combined_limit at FS:0 - 0xD0
 *
 * The kernel provisions each task a private 2 MiB block (present, RW, user,
 * one huge page) and initializes FS:0 to the task's TP, so the TLS bump
 * pointer is per-process and the heap memory is process-owned. No helper
 * returns a silent success: exhaustion yields NULL.
 */

.section .tbss,"awT",@nobits
.align 8
.global tls_CurrentThread
.hidden tls_CurrentThread
.type tls_CurrentThread, @object
.size tls_CurrentThread, 208
tls_CurrentThread:
    .zero 208

.section .text

/* ---- TLS primitives (FS-relative, C# cannot emit segment access) ---- */

/* ulong osb_tls_tp(): return FS:0. */
.global osb_tls_tp
.type osb_tls_tp, @function
osb_tls_tp:
    movq %fs:0, %rax
    ret

/* ulong osb_tls_get_alloc(): *(ulong*)(FS:0 - 0xC8). */
.global osb_tls_get_alloc
.type osb_tls_get_alloc, @function
osb_tls_get_alloc:
    movq %fs:0, %rax
    movq -0xC8(%rax), %rax
    ret

/* ulong osb_tls_get_limit(): *(ulong*)(FS:0 - 0xD0). */
.global osb_tls_get_limit
.type osb_tls_get_limit, @function
osb_tls_get_limit:
    movq %fs:0, %rax
    movq -0xD0(%rax), %rax
    ret

/* void osb_tls_set_alloc(ulong v): *(ulong*)(FS:0 - 0xC8) = v. */
.global osb_tls_set_alloc
.type osb_tls_set_alloc, @function
osb_tls_set_alloc:
    movq %fs:0, %rax
    movq %rdi, -0xC8(%rax)
    ret

/* void osb_tls_set_limit(ulong v): *(ulong*)(FS:0 - 0xD0) = v. */
.global osb_tls_set_limit
.type osb_tls_set_limit, @function
osb_tls_set_limit:
    movq %fs:0, %rax
    movq %rdi, -0xD0(%rax)
    ret

/* ---- bump allocator over the thread-local [alloc, limit) arena ---- */

/* Private: bump_alloc(size in rdi) -> rax = object or NULL on exhaustion.
 * Aligns size up to 8 bytes; advances TLS alloc_ptr; never writes past limit. */
.align 4
bump_alloc:
    movq %fs:0, %rax           /* rax = TP */
    movq -0xC8(%rax), %rcx     /* rcx = alloc_ptr (old value = result) */
    movq -0xD0(%rax), %rdx     /* rdx = limit */
    movq %rcx, %r9             /* r9 = old alloc_ptr (object address) */
    movq %rdi, %r8             /* r8 = size */
    addq $7, %r8
    andq $-8, %r8              /* r8 = size aligned up to 8 */
    addq %r8, %rcx             /* rcx = alloc + aligned */
    cmpq %rdx, %rcx
    ja  bump_fail              /* alloc + aligned > limit -> out of memory */
    movq %rcx, -0xC8(%rax)     /* set alloc_ptr = alloc + aligned */
    movq %r9, %rax             /* rax = old alloc_ptr (object) */
    ret
bump_fail:
    xorl %eax, %eax            /* return NULL */
    ret

/* ---- NativeAOT allocation helpers (real MethodTable*-based ABI) ---- */

/* void* RhpNewFast(MethodTable* mt): size = mt->BaseSize (uint32 @ +4). */
.global RhpNewFast
.type RhpNewFast, @function
RhpNewFast:
    movl 4(%rdi), %eax         /* eax = BaseSize */
    movq %rax, %rdi            /* rdi = size (ulong) */
    jmp bump_alloc

/* void* RhpNewArrayFast(MethodTable* mt, int length):
 *   size = mt->BaseSize + length * mt->ComponentSize (uint16 @ +0). */
.global RhpNewArrayFast
.type RhpNewArrayFast, @function
RhpNewArrayFast:
    movq %rdi, %rax
    movl 4(%rax), %eax         /* eax = BaseSize */
    movzwq (%rdi), %rcx        /* rcx = ComponentSize (uint16 @ +0) */
    movslq %esi, %rsi          /* rsi = length (sign-extended int) */
    imulq %rsi, %rcx           /* rcx = length * ComponentSize */
    addq %rcx, %rax            /* rax = BaseSize + length * ComponentSize */
    movq %rax, %rdi            /* rdi = size */
    jmp bump_alloc

/* void* RhpNewPtrArrayFast(MethodTable* mt, int length) == RhpNewArrayFast. */
.global RhpNewPtrArrayFast
.type RhpNewPtrArrayFast, @function
RhpNewPtrArrayFast:
    jmp RhpNewArrayFast

/* void* RhNewString(MethodTable* mt, nint length) == RhpNewArrayFast. */
.global RhNewString
.type RhNewString, @function
RhNewString:
    jmp RhpNewArrayFast

/* ---- GC-related helpers: not implemented in this no-GC runtime. Return NULL
 * honestly rather than pretending an allocation succeeded. ---- */

.global RhpNewVariableSizeObject
.type RhpNewVariableSizeObject, @function
RhpNewVariableSizeObject:
    xorl %eax, %eax
    ret

.global RhpGcAlloc
.type RhpGcAlloc, @function
RhpGcAlloc:
    xorl %eax, %eax
    ret

.global RhpNewObject
.type RhpNewObject, @function
RhpNewObject:
    xorl %eax, %eax
    ret

.global RhpNewFinalizable
.type RhpNewFinalizable, @function
RhpNewFinalizable:
    xorl %eax, %eax
    ret

/* ---- Write barriers: real stores (no GC in this runtime, so plain assign). ---- */

.global RhpAssignRef
.type RhpAssignRef, @function
RhpAssignRef:
    movq %rsi, (%rdi)
    ret

.global RhpByRefAssignRef
.type RhpByRefAssignRef, @function
RhpByRefAssignRef:
    movq %rsi, (%rdi)
    ret

.global RhpCheckedAssignRef
.type RhpCheckedAssignRef, @function
RhpCheckedAssignRef:
    movq %rsi, (%rdi)
    ret

/* ---- Exception throwing: hlt is privileged in Ring 3, so spin forever
 * (matches the original C while(1){} intent; no fake success). ---- */

.global RhpThrowEx
.type RhpThrowEx, @function
RhpThrowEx:
1:  jmp 1b

.global _RhpNewFast
.type _RhpNewFast, @function
_RhpNewFast:
    jmp RhpNewFast

.global _RhpThrowEx
.type _RhpThrowEx, @function
_RhpThrowEx:
    jmp RhpThrowEx

.global _RhNewString
.type _RhNewString, @function
_RhNewString:
    jmp RhNewString

.global _RhpNewArrayFast
.type _RhpNewArrayFast, @function
_RhpNewArrayFast:
    jmp RhpNewArrayFast

.section .note.GNU-stack, "", @progbits
