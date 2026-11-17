# 13b-nonroot: create a non-root user, log in as them (via the login:
# username prompt, not the LOGIN command), and check that privileged commands
# are refused while USER still lists. Login name/password use sendlf (LF only)
# so the legacy ReadLine leaves no stray CR/LF for the next prompt.
nologin
expect login:
sendlf root
prompt
send USER /ADD tester pw
prompt
sendnowait LOGOUT
expect login:
sendlf tester
expect Password:
sendlf pw
prompt
send WHOAMI
prompt
send USER
prompt
send USER /ADD intruder
prompt
send CHOWN root A:\OSB\ROOT
prompt
send SUDO VER
prompt