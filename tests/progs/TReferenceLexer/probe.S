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

/* ---- RhpNewArrayFast fix for freestanding NativeAOT ---- */
.global _RhpNewArrayFast
.type _RhpNewArrayFast, @function
_RhpNewArrayFast:
    /* Fixed RhpNewArrayFast for freestanding NativeAOT.
     * The compiler passes a vtable pointer (e.g., _ZTV13__Array<Char>) to
     * RhpNewArrayFast, but the vtable layout differs from the EEType layout
     * expected by the original helper:
     *   - Actual EEType: component size (WORD) at offset 0
     *   - Vtable: component size (WORD) at offset 8
     * We detect which layout by checking if offset 0 contains a valid
     * component size (1, 2, 4, or 8). If not, we read from offset 8.
     * rdi = vtable/EEType pointer, rsi = count
     */
    pushq %rbp
    movq %rsp, %rbp
    pushq %rbx
    pushq %r12
    pushq %r13
    
    movq %rdi, %rbx     /* save vtable/EEType pointer */
    movq %rsi, %r12     /* save count */
    
    /* Try to read component size from offset 0 */
    movzwl (%rbx), %eax
    cmpw $1, %ax
    je .L_has_component_size
    cmpw $2, %ax
    je .L_has_component_size
    cmpw $4, %ax
    je .L_has_component_size
    cmpw $8, %ax
    je .L_has_component_size
    
    /* Offset 0 is not a valid component size. Try offset 8 (vtable layout). */
    movzwl 8(%rbx), %eax
    cmpw $1, %ax
    je .L_has_component_size
    cmpw $2, %ax
    je .L_has_component_size
    cmpw $4, %ax
    je .L_has_component_size
    cmpw $8, %ax
    je .L_has_component_size
    
    /* Neither offset has a valid component size. Default to 2 (char). */
    movw $2, %ax
    
.L_has_component_size:
    movq %rax, %r13     /* r13 = component size */
    
    /* Calculate total size: 8 (MT) + 4 (length) + count * component_size, aligned to 8 */
    movq %r13, %rax
    imulq %r12, %rax          /* rax = count * component_size */
    addq $12, %rax            /* rax = 8 + 4 + data_size */
    addq $7, %rax
    andq $-8, %rax            /* align to 8 bytes */
    
    /* Get current allocation pointer from TLS */
    movq %fs:0, %rcx
    movq -0xC8(%rcx), %rdx    /* rdx = alloc_ptr */
    movq -0xD0(%rcx), %rcx    /* rcx = limit */
    
    addq %rax, %rdx
    cmpq %rcx, %rdx
    ja  .L_noarray            /* Out of memory */
    
    /* Allocate: subtract size from alloc_ptr to get object address */
    subq %rax, %rdx
    movq %rdx, -0xC8(%rcx)    /* Update alloc_ptr */
    
    /* rdx now points to the new array object */
    /* Write MethodTable (the vtable pointer we were given) */
    movq %rbx, (%rdx)
    /* Write Length */
    movl %r12d, 8(%rdx)
    /* Data starts at offset 12 */
    
    movq %rdx, %rax           /* Return pointer */
    jmp .L_done
    
.L_noarray:
    xorl %eax, %eax
    
.L_done:
    popq %r13
    popq %r12
    popq %rbx
    popq %rbp
    ret

.global _RhpNewPtrArrayFast
.type _RhpNewPtrArrayFast, @function
_RhpNewPtrArrayFast:
    jmp RhpNewPtrArrayFast

.global _RhNewString
.type _RhNewString, @function
_RhNewString:
    jmp RhNewString

.section .note.GNU-stack, "", @progbits