FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy shared FipShared project (referenced as ../../../shared/FipShared from forms/src/FortressFormTools.Web)
COPY shared/FipShared/ shared/FipShared/

# Copy project files for restore caching
COPY forms/src/FortressFormTools.Web/FortressFormTools.Web.csproj forms/src/FortressFormTools.Web/
COPY forms/src/FortressFormTools.Data/FortressFormTools.Data.csproj forms/src/FortressFormTools.Data/
RUN dotnet restore "forms/src/FortressFormTools.Web/FortressFormTools.Web.csproj"

# Copy full forms source
COPY forms/src/ forms/src/

WORKDIR "/src/forms/src/FortressFormTools.Web"
RUN dotnet build "FortressFormTools.Web.csproj" -c Release -o /app/build

FROM build AS publish
WORKDIR "/src/forms/src/FortressFormTools.Web"
RUN dotnet publish "FortressFormTools.Web.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
HEALTHCHECK --interval=30s --timeout=3s --start-period=30s --retries=3 \
    CMD curl -f http://localhost:8080/health || exit 1
ENTRYPOINT ["dotnet", "FortressFormTools.Web.dll"]
