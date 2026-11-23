/* TStringBuilder probe.S: TLS state provider for AllocFast_real.o wrappers.
 *
 * AllocFast_real.o provides wrapper functions (RhpNewArrayFast, RhNewString,
 * RhpNewPtrArrayFast, RhpNewFast) that:
 *   1. Do size calculation and bounds-check preparation
 *   2. Call _Xxx(tls_addr) which must return a pointer to [limit:8][alloc_ptr:8]
 *   3. Do bounds checking and object header writing
 *   4. Return the allocated object pointer
 *
 * This probe.S provides only the _Xxx entry points and TLS infrastructure.
 * It does NOT define RhpNewArrayFast/RhNewString/etc. (those come from
 * AllocFast_real.o and would cause duplicate-symbol conflicts).
 *
 * TLS layout:
 *   FS:0 - 0xD0 = limit (allocation ceiling)
 *   FS:0 - 0xC8 = alloc_ptr (allocation pointer, grows UPWARD)
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
1:  hlt
    jmp 1b

.global RhpAssignRef
.type RhpAssignRef, @function
RhpAssignRef:
    movq %rsi, (%rdi)
    movq %rsi, %rax
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

/* ---- Fallback stubs (NOT a GC allocator) ---- */

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

/* ---- _Xxx entry points for AllocFast_real.o wrappers ----
 *
 * Each _Xxx function receives rdi = TLS hint (from lea %rip in wrapper),
 * which is ignored. Returns rax = pointer to TLS state struct
 * at FS:0 - 0xD0, i.e. [limit:8][alloc_ptr:8].
 */

.global _RhpNewArrayFast
.type _RhpNewArrayFast, @function
_RhpNewArrayFast:
    movq %fs:0, %rax
    subq $0xD0, %rax
    ret

.global _RhpNewPtrArrayFast
.type _RhpNewPtrArrayFast, @function
_RhpNewPtrArrayFast:
    movq %fs:0, %rax
    subq $0xD0, %rax
    ret

.global _RhNewString
.type _RhNewString, @function
_RhNewString:
    movq %fs:0, %rax
    subq $0xD0, %rax
    ret

.global _RhpNewFast
.type _RhpNewFast, @function
_RhpNewFast:
    movq %fs:0, %rax
    subq $0xD0, %rax
    ret

.section .note.GNU-stack, "", @progbits