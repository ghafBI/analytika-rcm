# Windows process recovery test

- Test target: isolated production-recovery preview on `127.0.0.1:5102`
- Data boundary: dedicated `.preview-data` SQLite directory; the live 36 GB database was not modified
- Shutdown: graceful console interrupt logged at 2026-09-07 17:22:18 GST
- Restart: listener available at 2026-09-07 17:22:35 GST; approximately 17 seconds from shutdown event and approximately 1 second from new-process startup logs
- Health recovery: `/healthz` returned HTTP 200; verification request completed in 513 ms
- Log retention: existing log length grew from 7,667 to 9,984 bytes after restart; prior entries remained present
- Known preview-only warning: dashboard pre-warm found a missing optional index in the fresh demo database. It did not prevent liveness or authenticated workflows.

Authenticated workflow checks after restart returned HTTP 200 for Dashboard, RCM Dashboard, Claim Summary Report, Portal Fetch, and Denial Dashboard. The preview used the seeded development administrator and did not reuse production cookies or credentials.
