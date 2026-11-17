# 22-args (spec, min-level 1b): GETARGS (23h) returns the task's argument
# string. TArgs.EXE prints "args=[...]" from GETARGS. Booted with no args, so
# the string is empty. No login: the boot program IS the test.
nologin
boot A:\OSB\BIN\TArgs.EXE
expect args=[]