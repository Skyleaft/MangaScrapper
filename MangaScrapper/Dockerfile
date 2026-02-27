# Build Stage
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

# Copy project file and restore dependencies
COPY ["MangaScrapper/MangaScrapper.csproj", "MangaScrapper/"]
RUN dotnet restore "MangaScrapper/MangaScrapper.csproj"

# Copy all source files
COPY . .
WORKDIR "/src/MangaScrapper"

# Build and publish
RUN dotnet build "MangaScrapper.csproj" -c Release -o /app/build
RUN dotnet publish "MangaScrapper.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime Stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS final
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

# Create images directory
RUN mkdir -p /app/images

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "MangaScrapper.dll"]
