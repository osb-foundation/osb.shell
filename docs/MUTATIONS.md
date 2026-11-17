# Mutation testing — 1c identity/session checks (Q7)

Each patch in `tests/mutations/` breaks exactly one kernel security check. The
runner `tests/mutations.sh` applies a patch, rebuilds the ISO (WITH_TESTS=1),
boots the case that exercises the broken check, asserts the case FAILS, then
reverts the patch. A mutation is **caught** when the case no longer passes; a
mutation that still passes is a hole in the tests.

```
tests/mutations.sh            # all
tests/mutations.sh M01-nopass # one
```

Patches carry `a/ b/` prefixes and apply from the `osb.kernel` root with
`patch -p1`. The kernel working tree is expected to already contain the 1c.1
changes; patches are generated against that state.

## Results (current tree)

| Patch                    | Case           | Result | Failure observed            |
|--------------------------|----------------|--------|-----------------------------|
| M01-nopass               | 28-identity    | caught | TID: FAIL badpass           |
| M02-unknown-differs      | 28-identity    | caught | TID: FAIL unknown           |
| M03-login-isleader       | 28-identity    | caught | TID: FAIL childuid          |
| M04-logout-isleader      | 28-identity    | caught | TID: FAIL childuid          |
| M05-sudo-sudoers         | 28-identity    | caught | TID: FAIL sudo              |
| M06-endsudo-noop         | 30-sudo-scope  | caught | TSUDO: FAIL endsudo         |
| M07-uid-not-inherited    | 28-identity    | caught | TID: FAIL childuid          |
| M08-poweroff-unchecked   | 28-identity    | caught | TID: FAIL poweroff          |
| M09-exec-skipped         | 28-identity    | caught | TID: FAIL rootonly          |
| M10-logout-no-kill       | 29-logout-kills| caught | timeout (Wait never returns)|
| M11-name-truncation      | 28-identity    | caught | TID: FAIL longname          |

`caught=11 missed=0 broken=0`.

## What each mutation breaks

- **M01-nopass** — `Session.Login` skips the password comparison. A wrong
  password authenticates; `TId` check #2 (badpass) fails.
- **M02-unknown-differs** — `Login` returns `-2` for an unknown user instead of
  `-3`. Unknown-user and wrong-password become distinguishable; `TId` check #3
  (unknown) fails.
- **M03-login-isleader** — the `IsLeader` guard is dropped from `Login`. A
  non-leader child can LOGIN; `TId`'s child check (childuid) fails.
- **M04-logout-isleader** — the `IsLeader` guard is dropped from `Logout`. A
  non-leader child can LOGOUT; `TId`'s child check (childuid) fails.
- **M05-sudo-sudoers** — `Sudo` skips the SUDOERS membership check. A
  non-sudoer elevates; `TId` check #6 (sudo) fails.
- **M06-endsudo-noop** — `EndSudo` does not restore the real uid. `TSudo`
  check #2 (parent restored after END_SUDO) fails.
- **M07-uid-not-inherited** — `SpawnFileInode` seeds a child with uid/realUid
  -1 instead of the parent's. A child's WHOAMI is no longer the session user;
  `TId`'s child check (childuid) fails. (Ring-3 programs spawn via 6Ah ->
  `SpawnFileInode`, so this is the path the tests exercise.)
- **M08-poweroff-unchecked** — the root check is dropped from POWEROFF (0x58).
  A non-root caller is no longer refused; `TId` check #7 (poweroff) fails.
- **M09-exec-skipped** — `SpawnExecutable` returns true unconditionally. A
  non-root caller can exec a root-owned 0700 file; `TId` check #9 (rootonly)
  fails.
- **M10-logout-no-kill** — `TeardownSession` does not `Release` session tasks.
  `TLogout`'s `Wait` on the un-killed child never returns, so the case times
  out (caught).
- **M11-name-truncation** — `Login` truncates an over-long name to `NameCap-1`
  instead of rejecting it. The 17-char name collapses to the seeded 15-char
  account and authenticates; `TId` check #4 (longname) fails.

## Notes

- M10 is caught by timeout rather than an assertion line: with the child alive,
  `Wait(tpid)` blocks forever and the driver's overall timeout fires. This is
  the expected failure mode for "LOGOUT does not kill the session".
- M08 returns success to any caller instead of powering off, so the run is fast
  and does not tear down QEMU; the check under test (root-only POWEROFF) is what
  breaks.