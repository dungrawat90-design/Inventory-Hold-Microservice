# Build context is the repository root:  docker build -f src/InventoryHold.Api/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props .editorconfig ./
COPY src/ src/
RUN dotnet restore src/InventoryHold.Api/InventoryHold.Api.csproj
# Warnings are errors in the repo build, but the image must not break because a newer SDK patch adds a warning.
RUN dotnet publish src/InventoryHold.Api/InventoryHold.Api.csproj -c Release -o /app --no-restore -p:TreatWarningsAsErrors=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_EnableDiagnostics=0
# The aspnet image ships a non-root user; never run the service as root.
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "InventoryHold.Api.dll"]
