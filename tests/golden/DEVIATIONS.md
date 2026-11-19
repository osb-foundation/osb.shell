# Approved deviations (STAGE-B-PLAN §0)

- Legacy in-kernel shell removed before all commands migrated.
- GREP, NET/IFCONFIG, PING, PCI deferred (network commands later). GREP and `|` pipes not wanted in present form.
- POWEROFF/SHUTDOWN/QUIT, COLOR, DELETE/ERASE, RENAME, LNK, RING3 dropped on purpose.
- OSLANG leaves kernel (separate plan).
