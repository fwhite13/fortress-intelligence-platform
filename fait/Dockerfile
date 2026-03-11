FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080
RUN apt-get update && apt-get install -y --no-install-recommends \
    curl \
    libreoffice \
    && rm -rf /var/lib/apt/lists/*

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy project files for layer caching
COPY src/FortressAI.Shared/FortressAI.Shared.csproj FortressAI.Shared/
COPY src/FortressAI.Web/FortressAI.Web.csproj FortressAI.Web/
RUN dotnet restore "FortressAI.Web/FortressAI.Web.csproj"

# Copy source and build
COPY src/ .
WORKDIR "/src/FortressAI.Web"
RUN dotnet build "FortressAI.Web.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "FortressAI.Web.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
RUN mkdir -p /app/keys && chmod 755 /app/keys
COPY --from=publish /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
HEALTHCHECK --interval=30s --timeout=3s --start-period=30s --retries=3 \
    CMD curl -f http://localhost:8080/health || exit 1
ENTRYPOINT ["dotnet", "FortressAI.Web.dll"]
