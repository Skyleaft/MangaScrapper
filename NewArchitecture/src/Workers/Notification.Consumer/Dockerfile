# ─────────────────────────────────────────────────────────────────────────────
# Notification.Consumer — Worker Dockerfile
# ─────────────────────────────────────────────────────────────────────────────

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
RUN apt-get update && apt-get install -y --no-install-recommends \
    clang \
    zlib1g-dev \
    && rm -rf /var/lib/lists/*

WORKDIR /src

COPY ["src/BuildingBlocks/NovaStack.SharedKernel/NovaStack.SharedKernel.csproj", "src/BuildingBlocks/NovaStack.SharedKernel/"]
COPY ["src/BuildingBlocks/NovaStack.Infrastructure/NovaStack.Infrastructure.csproj", "src/BuildingBlocks/NovaStack.Infrastructure/"]
COPY ["src/BuildingBlocks/NovaStack.Contracts/NovaStack.Contracts.csproj", "src/BuildingBlocks/NovaStack.Contracts/"]
COPY ["src/Workers/Notification.Consumer/Notification.Consumer.csproj", "src/Workers/Notification.Consumer/"]

RUN dotnet restore "src/Workers/Notification.Consumer/Notification.Consumer.csproj"

COPY . .
WORKDIR /src/src/Workers/Notification.Consumer
RUN dotnet publish "Notification.Consumer.csproj" -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0 AS runtime
WORKDIR /app

RUN addgroup --system appgroup && adduser --system --ingroup appgroup appuser
COPY --from=build /app/publish .
RUN chown -R appuser:appgroup /app
USER appuser

ENTRYPOINT ["./Notification.Consumer"]
