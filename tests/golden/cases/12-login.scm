# 12-login: WHOAMI, LOGIN (passwordless for root), WHOAMI, LOGOUT back to
# the login prompt. nologin: the script drives login itself, using expect
# "login:" before sending the name (no sleeps).
nologin
expect login:
send root
prompt
send WHOAMI
prompt
sendnowait LOGIN
expect login:
send root
prompt
send WHOAMI
prompt
sendnowait LOGOUT
expect login: