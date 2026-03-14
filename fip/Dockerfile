FROM mcr.microsoft.com/dotnet/aspnet:8.0-bookworm-slim AS base
WORKDIR /app
EXPOSE 80

FROM mcr.microsoft.com/dotnet/sdk:8.0-bookworm-slim AS build
WORKDIR /src
COPY ["src/FortressIntelligencePlatform.Web/FortressIntelligencePlatform.Web.csproj", "FortressIntelligencePlatform.Web/"]
RUN dotnet restore "FortressIntelligencePlatform.Web/FortressIntelligencePlatform.Web.csproj"
COPY src/FortressIntelligencePlatform.Web/ FortressIntelligencePlatform.Web/
WORKDIR "/src/FortressIntelligencePlatform.Web"
RUN dotnet build "FortressIntelligencePlatform.Web.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "FortressIntelligencePlatform.Web.csproj" -c Release -o /app/publish --no-restore

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "FortressIntelligencePlatform.Web.dll"]
