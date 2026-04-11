FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /app

COPY Directory.Build.props .
COPY SmartCollect.Api.csproj .
RUN dotnet restore

COPY . .
RUN dotnet publish SmartCollect.Api.csproj -c Release -o /out --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /out .

EXPOSE 5013
ENV ASPNETCORE_URLS=http://+:5013
ENV ASPNETCORE_ENVIRONMENT=Development

ENTRYPOINT ["dotnet", "SmartCollect.Api.dll"]
