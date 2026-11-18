# 16-jobs: TASK, DEMO (ctrl-c aborts a foreground job), KILL, WAIT.
send TASK
prompt
sendnowait DEMO
# Wait for the shell's "Foreground [pid]" line instead of a fixed sleep:
# DEMO is already the foreground task once that line is printed.
expect Foreground [
ctrl-c
expect Foreground job aborted
prompt
send KILL 999
prompt
send WAIT 999
prompt