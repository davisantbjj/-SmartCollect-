# SmartCollect

Plataforma de cobranca multi-tenant com frontend React + backend ASP.NET Core.

Este repositorio segue estrutura monorepo, com entrada principal na raiz.

## Estrutura

- `frontend/`: aplicação web (Vite + React + TypeScript)
- `backend/`: API, regras de negocio e persistencia (.NET + EF Core)
- `docker-compose.yml`: orquestracao oficial (PostgreSQL, API e pgAdmin)
- `.env.example`: variaveis de ambiente base
- `frontend/.env.local.example`: variaveis de frontend para desenvolvimento local

## Arquitetura

- Frontend: React 19, Vite 8, Tailwind
- Backend: ASP.NET Core, EF Core, JWT, MailKit
- Banco: PostgreSQL 16

Detalhes da arquitetura por camada em [ARCHITECTURE.md](ARCHITECTURE.md).

## Fluxo Oficial de Execucao

Sempre execute pela raiz do projeto.

1. Copie os arquivos de ambiente:
   - Copie `.env.example` para `.env`
   - Copie `frontend/.env.local.example` para `frontend/.env.local`

2. Comando unico (recomendado no dia a dia):
   - `npm run dev:all`

   Esse comando:
   - sobe db/api/pgadmin via Docker Compose
   - instala dependencias npm se necessario
   - inicia o frontend em modo dev

3. Fluxo manual (alternativo):
   - `docker compose up -d --build`

4. Suba frontend:
   - `cd frontend && npm install && npm run dev`

Para parar os servicos Docker:
   - `npm run stop:all`

## Endpoints Locais

- Frontend: http://localhost:5173
- API: http://localhost:5013
- Swagger: http://localhost:5013/swagger
- PostgreSQL: localhost:5432
- pgAdmin: http://localhost:8080

## Validacoes Recomendadas Antes de Publicar

- Backend build: `dotnet build backend/SmartCollect.Api.csproj`
- Backend testes: `dotnet test backend/Tests/SmartCollect.Tests.csproj`
- Frontend build: `npm run build --prefix frontend`

## Politica de Ambientes

- Arquivo principal de ambiente do projeto: `.env` na raiz
- Frontend local: `frontend/.env.local`
- Nao utilizar `backend/.env` como fonte principal

## Organizacao Para Git

- Nao versionar `node_modules`, `dist`, `bin`, `obj` ou arquivos `.env`
- Versionar apenas exemplos de ambiente (`.env.example`, `.env.local.example`)
- Evitar duplicidade de compose e documentacao desatualizada
