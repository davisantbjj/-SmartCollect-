# SmartCollect Backend

API ASP.NET Core responsavel por autenticacao, regras de cobranca, templates, disparos e integracoes.

## Estrutura Interna

- `Controllers/`: endpoints HTTP
- `Application/`: DTOs, interfaces e servicos de negocio
- `Domain/`: entidades e enums
- `Infrastructure/`: DbContext, repositorios, DI
- `Migrations/`: migracoes EF Core
- `Tests/`: testes xUnit

## Build

- `dotnet build SmartCollect.Api.csproj`

## Testes

- `dotnet test Tests/SmartCollect.Tests.csproj`

## Run Local (sem Docker)

A API prioriza o arquivo `.env` da raiz do repositorio.

- `dotnet run --project SmartCollect.Api.csproj`

## Run com Docker

Fluxo canônico do projeto fica na raiz:

- `docker compose up -d --build`

Evite manter compose paralelo dentro de `backend/` para nao gerar ambiguidade de ambiente.
