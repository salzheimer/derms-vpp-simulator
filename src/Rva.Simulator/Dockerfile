# Rva.Simulator: the Chaos Engine, a separate process that reaches the backend only over HTTP and MQTT. Build context
# is the repo root, like the backend's (global.json and Directory.Packages.props):
#   docker build -f src/Rva.Simulator/Dockerfile -t rva-simulator .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY global.json Directory.Packages.props ./
COPY src/Rva.Simulator/ src/Rva.Simulator/
RUN dotnet restore src/Rva.Simulator/Rva.Simulator.csproj
RUN dotnet publish src/Rva.Simulator/Rva.Simulator.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app
COPY --from=build /app .

USER $APP_UID
ENTRYPOINT ["dotnet", "Rva.Simulator.dll"]
