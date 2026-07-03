# --- Build stage ---
# Build context is the repo root (see docker-compose.yml).
FROM mcr.microsoft.com/dotnet/sdk:10.0.300 AS build
WORKDIR /src

# Restore first (cached unless the csproj changes).
COPY SiaWebhookApi/SiaWebhookApi.csproj SiaWebhookApi/
RUN dotnet restore SiaWebhookApi/SiaWebhookApi.csproj

# Then build + publish.
COPY SiaWebhookApi/ SiaWebhookApi/
RUN dotnet publish SiaWebhookApi/SiaWebhookApi.csproj -c Release -o /app/publish /p:UseAppHost=false

# --- Runtime stage ---
FROM mcr.microsoft.com/dotnet/aspnet:10.0.3 AS runtime
WORKDIR /app

# curl is used by the container healthcheck (the runtime image ships without it).
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* \
    && groupadd --gid 10001 app \
    && useradd --uid 10001 --gid app --home-dir /app --shell /usr/sbin/nologin app \
    && mkdir -p /app/data \
    && chown -R app:app /app

COPY --from=build --chown=app:app /app/publish .

# Listen on 8080 inside the container (proxied by Coolify/Traefik).
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

USER app
ENTRYPOINT ["dotnet", "SiaWebhookApi.dll"]
