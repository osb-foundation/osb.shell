/* M4.1 TOslangProof: TLS access and fallback stubs for NativeAOT helpers */

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

/* ---- fallback stubs (NOT a GC allocator) ---- */

.global RhpNewVariableSizeObject
.type RhpNewVariableSizeObject, @function
RhpNewVariableSizeObject:
    xorl %eax, %eax
    ret

.global RhExceptionHandling_FailedAllocation
.type RhExceptionHandling_FailedAllocation, @function
RhExceptionHandling_FailedAllocation:
    /* Exit with error code 1 instead of hlt (privileged in ring 3) */
    movq $0x4C01, %rax  /* INT 21h AH=4Ch Exit with code 1 */
    int  $0x21
1:  jmp 1b

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

/* Additional stubs needed by MiniCoreLib */
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

.global RhpThrowEx
.type RhpThrowEx, @function
RhpThrowEx:
    /* Exit with error code 1 instead of hlt (privileged in ring 3) */
    movq $0x4C01, %rax  /* INT 21h AH=4Ch Exit with code 1 */
    int  $0x21
1:  jmp 1b

.global RhpCheckedAssignRef
.type RhpCheckedAssignRef, @function
RhpCheckedAssignRef:
    /* Signature: void RhpCheckedAssignRef(void** dest, void* src, ulong index, ulong length) */
    /* Simple unchecked version for freestanding */
    movq %rsi, (%rdi)
    ret

.section .note.GNU-stack, "", @progbits