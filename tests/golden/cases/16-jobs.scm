# 16-jobs: TASK, DEMO (ctrl-c aborts a foreground job), KILL, WAIT.
send TASK
prompt
sendnowait DEMO
sleep 2
ctrl-c
expect Foreground job aborted
prompt
send KILL 999
prompt
send WAIT 999
prompt