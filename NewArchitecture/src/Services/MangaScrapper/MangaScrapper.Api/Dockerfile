FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution and project files for caching restore
COPY ["NewArchitecture/MangaScrapperStack.sln", "NewArchitecture/"]
COPY ["NewArchitecture/src/BuildingBlocks/NovaStack.SharedKernel/NovaStack.SharedKernel.csproj", "NewArchitecture/src/BuildingBlocks/NovaStack.SharedKernel/"]
COPY ["NewArchitecture/src/BuildingBlocks/NovaStack.Infrastructure/NovaStack.Infrastructure.csproj", "NewArchitecture/src/BuildingBlocks/NovaStack.Infrastructure/"]
COPY ["NewArchitecture/src/BuildingBlocks/NovaStack.Contracts/NovaStack.Contracts.csproj", "NewArchitecture/src/BuildingBlocks/NovaStack.Contracts/"]
COPY ["NewArchitecture/src/Services/MangaScrapper/MangaScrapper.Domain/MangaScrapper.Domain.csproj", "NewArchitecture/src/Services/MangaScrapper/MangaScrapper.Domain/"]
COPY ["NewArchitecture/src/Services/MangaScrapper/MangaScrapper.Application/MangaScrapper.Application.csproj", "NewArchitecture/src/Services/MangaScrapper/MangaScrapper.Application/"]
COPY ["NewArchitecture/src/Services/MangaScrapper/MangaScrapper.Infrastructure/MangaScrapper.Infrastructure.csproj", "NewArchitecture/src/Services/MangaScrapper/MangaScrapper.Infrastructure/"]
COPY ["NewArchitecture/src/Services/MangaScrapper/MangaScrapper.Api/MangaScrapper.Api.csproj", "NewArchitecture/src/Services/MangaScrapper/MangaScrapper.Api/"]

RUN dotnet restore "NewArchitecture/src/Services/MangaScrapper/MangaScrapper.Api/MangaScrapper.Api.csproj"

# Copy full source and publish
COPY . .
WORKDIR "/src/NewArchitecture/src/Services/MangaScrapper/MangaScrapper.Api"
RUN dotnet publish "MangaScrapper.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "MangaScrapper.Api.dll"]
