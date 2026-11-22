/* M3.4.2 Ring-3 TLS probe.
 *
 * These are the exact FS-relative accesses the NativeAOT linker emits for the
 * Local Exec TLS model (see the M3.4.1 report):
 *
 *     mov %fs:0, %rax          ; TP = thread pointer
 *     lea -0xD0(%rax), %rax    ; &tls_CurrentThread = TP - 0xD0
 *     mov 0x0(%rax), %rax      ; combined_limit
 *     mov 0x8(%rax), %rax      ; alloc_ptr
 *
 * No C global and no simulated pointer: every value below is read through the
 * %fs segment. The kernel sets IA32_FS_BASE to the task's thread pointer
 * before it enters ring 3 and on every switch-in (M3.4.2).
 */

.section .text

/* ulong osb_tls_tp(): return FS:0 (the thread pointer). */
.global osb_tls_tp
.type osb_tls_tp, @function
osb_tls_tp:
    movq %fs:0, %rax
    ret

/* ulong osb_tls_obj(): return FS:0 - 0xD0 (address of tls_CurrentThread). */
.global osb_tls_obj
.type osb_tls_obj, @function
osb_tls_obj:
    movq %fs:0, %rax
    subq $0xD0, %rax
    ret

/* ulong osb_tls_get_limit(): return *(ulong*)(FS:0 - 0xD0) = combined_limit. */
.global osb_tls_get_limit
.type osb_tls_get_limit, @function
osb_tls_get_limit:
    movq %fs:0, %rax
    movq -0xD0(%rax), %rax
    ret

/* ulong osb_tls_get_alloc(): return *(ulong*)(FS:0 - 0xC8) = alloc_ptr. */
.global osb_tls_get_alloc
.type osb_tls_get_alloc, @function
osb_tls_get_alloc:
    movq %fs:0, %rax
    movq -0xC8(%rax), %rax
    ret

/* void osb_tls_set_limit(ulong v): *(ulong*)(FS:0 - 0xD0) = v. */
.global osb_tls_set_limit
.type osb_tls_set_limit, @function
osb_tls_set_limit:
    movq %fs:0, %rax
    movq %rdi, -0xD0(%rax)
    ret

/* void osb_tls_set_alloc(ulong v): *(ulong*)(FS:0 - 0xC8) = v. */
.global osb_tls_set_alloc
.type osb_tls_set_alloc, @function
osb_tls_set_alloc:
    movq %fs:0, %rax
    movq %rdi, -0xC8(%rax)
    ret

.section .note.GNU-stack, "", @progbits