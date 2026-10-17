# ADR-0003: Template Service Clean Architecture

- **Status:** Diterima
- **Tanggal:** 2026-10-17

## Konteks

Ketujuh service (Auth, User, Project, Task, Sop, Notification, Audit) harus memiliki struktur dan pola yang sama agar mudah dipelihara (ARCHITECTURE.md D.1–D.2).

## Keputusan

Setiap service berada di `src/Services/<Nama>/` dengan project:

| Project | Isi | Boleh bergantung pada |
| --- | --- | --- |
| `NexUS.<Nama>.Domain` | entity, aturan bisnis | `BuildingBlocks.Common` |
| `NexUS.<Nama>.Application` | MediatR, FluentValidation, `ValidationBehavior`, `LoggingBehavior` | Domain |
| `NexUS.<Nama>.Infrastructure` | Oracle, Redis, broker | Application |
| `NexUS.<Nama>.Api` | Program.cs, Swagger, `ExceptionHandlingMiddleware`, `/health/live`, `/health/ready` | Application, Infrastructure, Observability |
| `tests/NexUS.<Nama>.UnitTests` | test arsitektur (NetArchTest) dan behavior | semua layer |
| `tests/NexUS.<Nama>.IntegrationTests` | `WebApplicationFactory`; Testcontainers ditambah saat ada akses data | Api |

Port lokal: Auth 5101, User 5102, Project 5103, Task 5104, Sop 5105, Notification 5106, Audit 5107.

Pemetaan exception ke ProblemDetails: `ValidationException` 400, `ForbiddenException` 403, `NotFoundException` 404, `ConcurrencyException` 409, `DomainException` 422, lainnya 500 tanpa detail.

### Pengecualian namespace service Task

Namespace `NexUS.Task` membayangi `System.Threading.Tasks.Task` di seluruh kode `NexUS.*`, sehingga `Task<T>` tidak bisa dikompilasi. Nama project dan assembly tetap `NexUS.Task.*`, tetapi `RootNamespace`-nya adalah `NexUS.Tasks.*`.

## Konsekuensi

- Service baru disalin dari pola ini; perubahan pola dicatat sebagai ADR baru.
- `ValidationBehavior`, `LoggingBehavior`, dan `ExceptionHandlingMiddleware` diduplikasi per service. Bila nanti terlalu mahal dirawat, dipindah ke BuildingBlocks lewat ADR.
- Infrastructure masih kosong; diisi saat story fitur masing-masing service.
