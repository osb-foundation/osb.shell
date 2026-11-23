/* M3.4.2 Phase 6: link the real NativeAOT allocation helpers into a Ring 3
 * program.
 *
 * - tls_CurrentThread: the 208-byte TLS object the helpers address as
 *   FS:0 - 0xD0. The linker relaxes the helpers' R_X86_64_TLSGD relocations
 *   to Local Exec (mov %fs:0,%rax ; lea -0xD0(%rax),%rax) because this is the
 *   only TLS object and it is hidden. No ELF TLS loader is needed: the kernel
 *   initializes the object at TP-0xD0 (Scheduler.InitTaskTls) and sets FS base.
 *
 * - Fallback stubs: the real GC path is not linked. RhpNewVariableSizeObject
 *   returns NULL, RhExceptionHandling_FailedAllocation halts. The success test
 *   never reaches them (the bounded heap is large enough); the report must not
 *   claim fallback allocation works.
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

/* ---- Exception handling stubs ---- */

.global RhpThrowEx
.type RhpThrowEx, @function
RhpThrowEx:
    /* Exception thrown - halt */
1:  hlt
    jmp 1b

.global RhpAssignRef
.type RhpAssignRef, @function
RhpAssignRef:
    /* dst = src; return src */
    movq %rsi, (%rdi)
    movq %rsi, %rax
    ret

/* ---- fallback stubs (NOT a GC allocator) ---- */

.global RhpNewVariableSizeObject
.type RhpNewVariableSizeObject, @function
RhpNewVariableSizeObject:
    xorl %eax, %eax
    ret

.global RhExceptionHandling_FailedAllocation
.type RhExceptionHandling_FailedAllocation, @function
RhExceptionHandling_FailedAllocation:
1:  hlt
    jmp 1b

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

.section .note.GNU-stack, "", @progbits