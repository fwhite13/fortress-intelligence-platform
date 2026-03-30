FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080
RUN apt-get update && apt-get install -y --no-install-recommends \
    curl \
    && apt-get clean \
    && rm -rf /var/lib/apt/lists/*

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy project files for layer caching
COPY nexus/src/FortressNexus.Web/FortressNexus.Web.csproj nexus/src/FortressNexus.Web/
# Copy FipShared RCL (shared component library)
COPY shared/FipShared/FipShared.csproj shared/FipShared/
RUN dotnet restore "nexus/src/FortressNexus.Web/FortressNexus.Web.csproj"

# Copy source and build
COPY nexus/src/ nexus/src/
COPY shared/FipShared/ shared/FipShared/
WORKDIR "/src/nexus/src/FortressNexus.Web"
RUN dotnet build "FortressNexus.Web.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "FortressNexus.Web.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
HEALTHCHECK --interval=30s --timeout=3s --start-period=30s --retries=3 \
    CMD curl -f http://localhost:8080/health || exit 1
ENTRYPOINT ["dotnet", "FortressNexus.Web.dll"]
