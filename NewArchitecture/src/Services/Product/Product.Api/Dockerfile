# ─────────────────────────────────────────────────────────────────────────────
# Product.Api — Multi-stage Dockerfile
# ─────────────────────────────────────────────────────────────────────────────

# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution and project files first for layer caching
COPY ["NovaStack.sln", "./"]
COPY ["src/BuildingBlocks/NovaStack.SharedKernel/NovaStack.SharedKernel.csproj", "src/BuildingBlocks/NovaStack.SharedKernel/"]
COPY ["src/BuildingBlocks/NovaStack.Infrastructure/NovaStack.Infrastructure.csproj", "src/BuildingBlocks/NovaStack.Infrastructure/"]
COPY ["src/BuildingBlocks/NovaStack.Contracts/NovaStack.Contracts.csproj", "src/BuildingBlocks/NovaStack.Contracts/"]
COPY ["src/Services/Product.Domain/Product.Domain.csproj", "src/Services/Product.Domain/"]
COPY ["src/Services/Product.Application/Product.Application.csproj", "src/Services/Product.Application/"]
COPY ["src/Services/Product.Infrastructure/Product.Infrastructure.csproj", "src/Services/Product.Infrastructure/"]
COPY ["src/Services/Product.Api/Product.Api.csproj", "src/Services/Product.Api/"]

RUN dotnet restore "src/Services/Product.Api/Product.Api.csproj"

# Copy the rest and build
COPY . .
WORKDIR /src/src/Services/Product.Api
RUN dotnet publish "Product.Api.csproj" -c Release -o /app/publish --no-restore

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Create non-root user
RUN addgroup --system appgroup && adduser --system --ingroup appgroup appuser

# Copy published output
COPY --from=build /app/publish .
RUN chown -R appuser:appgroup /app

USER appuser

EXPOSE 8080
EXPOSE 8081

ENTRYPOINT ["dotnet", "Product.Api.dll"]
