# Rva.Host: the backend modular monolith. Build context is the repo root, which holds global.json and the central
# Directory.Packages.props the build needs (the root .dockerignore keeps everything else out):
#   docker build -f src/Rva.Backend/Rva.Host/Dockerfile -t rva-backend .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY global.json Directory.Packages.props ./
COPY src/Rva.Backend/ src/Rva.Backend/
RUN dotnet restore src/Rva.Backend/Rva.Host/Rva.Host.csproj
RUN dotnet publish src/Rva.Backend/Rva.Host/Rva.Host.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .

# Non-root user built into the .NET images; ASP.NET Core listens on 8080 by default (ASPNETCORE_HTTP_PORTS).
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Rva.Host.dll"]
