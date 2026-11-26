# Build stage: full SDK, only used to compile.
FROM mcr.microsoft.com/dotnet/sdk:11.0 AS build
WORKDIR /src

# Restore first so this layer is cached until a project file changes.
COPY src/Flagpole.Core/Flagpole.Core.csproj src/Flagpole.Core/
COPY src/Flagpole.Api/Flagpole.Api.csproj src/Flagpole.Api/
RUN dotnet restore src/Flagpole.Api/Flagpole.Api.csproj

COPY src/ src/
RUN dotnet publish src/Flagpole.Api/Flagpole.Api.csproj -c Release -o /app --no-restore

# Runtime stage: chiseled image with no shell or package manager, which
# leaves very little for an attacker to use. It runs as a non-root user.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS runtime
WORKDIR /app
COPY --from=build /app .

USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Flagpole.Api.dll"]
