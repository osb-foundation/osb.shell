/* M3.4.2 Phase 6: link the real NativeAOT allocation helpers into a Ring 3
 * program.
 *
 * - tls_CurrentThread: the 208-byte TLS object the helpers address as
 *   FS:0 - 0xD0. The linker relaxes the helpers' R_X86_64_TLSGD relocations
 *   to Local Exec (mov %fs:0,%rax ; lea -0xD0(%rax),%rax) because this is the
 *   only TLS object and it is hidden. No ELF TLS loader is needed: the kernel
 *   initializes the object at TP-0xD0 (Scheduler.InitTaskTls) and sets FS base.
 *
 * - Real allocation helpers from AllocFast_real.o (weakened) are linked after
 *   this probe so their strong definitions override our fallback stubs.
 *   Fallback stubs here are used only by programs without AllocFast_real.o.
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

/* ---- fallback stubs (NOT a GC allocator) ----
 * These are WEAK so that AllocFast_real.o strong definitions override them.
 * Only used by programs that do not link AllocFast_real.o. */

.global RhpNewVariableSizeObject
.weak RhpNewVariableSizeObject
.type RhpNewVariableSizeObject, @function
RhpNewVariableSizeObject:
    xorl %eax, %eax
    ret

.global RhExceptionHandling_FailedAllocation
.weak RhExceptionHandling_FailedAllocation
.type RhExceptionHandling_FailedAllocation, @function
RhExceptionHandling_FailedAllocation:
    /* Exit with error code 1 instead of hlt (privileged in ring 3) */
    movq $0x4C01, %rax  /* INT 21h AH=4Ch Exit with code 1 */
    int  $0x21
1:  jmp 1b

.global RhpGcAlloc
.weak RhpGcAlloc
.type RhpGcAlloc, @function
RhpGcAlloc:
    xorl %eax, %eax
    ret

.global RhpNewObject
.weak RhpNewObject
.type RhpNewObject, @function
RhpNewObject:
    xorl %eax, %eax
    ret

.global RhpNewFinalizable
.weak RhpNewFinalizable
.type RhpNewFinalizable, @function
RhpNewFinalizable:
    xorl %eax, %eax
    ret

/* Additional stubs needed by MiniCoreLib */
.global RhpAssignRef
.weak RhpAssignRef
.type RhpAssignRef, @function
RhpAssignRef:
    movq %rsi, (%rdi)
    ret

.global RhpByRefAssignRef
.weak RhpByRefAssignRef
.type RhpByRefAssignRef, @function
RhpByRefAssignRef:
    movq %rsi, (%rdi)
    ret

.global RhpThrowEx
.weak RhpThrowEx
.type RhpThrowEx, @function
RhpThrowEx:
    /* Exit with error code 1 instead of hlt (privileged in ring 3) */
    movq $0x4C01, %rax  /* INT 21h AH=4Ch Exit with code 1 */
    int  $0x21
1:  jmp 1b

.global RhpCheckedAssignRef
.weak RhpCheckedAssignRef
.type RhpCheckedAssignRef, @function
RhpCheckedAssignRef:
    /* Signature: void RhpCheckedAssignRef(void** dest, void* src, ulong index, ulong length) */
    /* Simple unchecked version for freestanding */
    movq %rsi, (%rdi)
    ret

/* Additional symbols for string/array allocation - WEAK for AllocFast_real.o override */
.global RhpNewFast
.weak RhpNewFast
.type RhpNewFast, @function
RhpNewFast:
    xorl %eax, %eax
    ret

.global RhNewString
.weak RhNewString
.type RhNewString, @function
RhNewString:
    xorl %eax, %eax
    ret

.global RhpNewArrayFast
.weak RhpNewArrayFast
.type RhpNewArrayFast, @function
RhpNewArrayFast:
    xorl %eax, %eax
    ret

.global _RhpNewFast
.weak _RhpNewFast
.type _RhpNewFast, @function
_RhpNewFast:
    jmp RhpNewFast

.global _RhpThrowEx
.weak _RhpThrowEx
.type _RhpThrowEx, @function
_RhpThrowEx:
    jmp RhpThrowEx

.global _RhNewString
.weak _RhNewString
.type _RhNewString, @function
_RhNewString:
    jmp RhNewString

.global _RhpNewArrayFast
.weak _RhpNewArrayFast
.type _RhpNewArrayFast, @function
_RhpNewArrayFast:
    jmp RhpNewArrayFast

.section .note.GNU-stack, "", @progbits