# SmartCollect Architecture Guide

## Objective
This project is split into three clear layers:
- Frontend: React + Vite application in `frontend/` (`frontend/src/`, `frontend/public/`)
- Backend: ASP.NET Core API in `backend/`
- Infrastructure: Docker and runtime configuration in root (`docker-compose.yml`, `.env`, `.env.example`, `frontend/.env.local.example`)

## Business Roles
- Master:
  - Owner role for Atos Capital
  - Can manage tenants (companies)
  - Can view Dashboard and Analytics with tenant selector (single tenant or aggregate)
  - Does not operate daily collection workflows
- Admin:
  - Tenant manager role
  - Manages workers
  - Configures templates, collection rules, and integrations for its tenant
- Worker:
  - Daily operational role
  - Handles operational collection workflows

## Frontend Structure
- `frontend/src/pages/`: route-level screens
- `frontend/src/components/`: reusable UI/layout pieces
- `frontend/src/services/api.ts`: backend contract and HTTP client
- `frontend/src/i18n/`: translations

## Backend Structure
- `backend/Domain/`: entities and enums (pure domain)
- `backend/Application/`: DTOs, interfaces, and business services
- `backend/Infrastructure/`: EF Core DbContext, repositories, DI wiring
- `backend/Controllers/`: HTTP entrypoints and role-based access

## Role and Tenant Rules
- Master has cross-tenant visibility for analytics endpoints and tenant management
- Admin/Worker are tenant-scoped through `TenantId` claim
- Tenant resolution should use centralized helper (`backend/Security/TenantContextResolver.cs`)

## Environment Files
- Project environment (canonical): `.env` in repository root
- Frontend local environment: `frontend/.env.local` (see `frontend/.env.local.example`)
- Backend local run: reads root `.env` first, with fallback only when root file is absent

## Validation Pipeline
Recommended local checks before delivery:
1. `dotnet build backend/SmartCollect.Api.csproj`
2. `dotnet ef database update --project backend/SmartCollect.Api.csproj --startup-project backend/SmartCollect.Api.csproj`
3. `npm run build --prefix frontend`
4. `npx --prefix frontend tsc --noEmit`

## Next Improvements
- Add integration tests for role boundaries (Master/Admin/Worker)
- Add end-to-end tests for login, dashboard tenant switch, and worker management
- Add CI pipeline gates for backend + frontend builds
