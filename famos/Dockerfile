FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY shared/FipShared/ shared/FipShared/
COPY famos/src/FamOs.Web/FamOs.Web.csproj famos/src/FamOs.Web/
RUN dotnet restore "famos/src/FamOs.Web/FamOs.Web.csproj"

COPY famos/src/ famos/src/

WORKDIR "/src/famos/src/FamOs.Web"
RUN dotnet build "FamOs.Web.csproj" -c Release -o /app/build

FROM build AS publish
WORKDIR "/src/famos/src/FamOs.Web"
RUN dotnet publish "FamOs.Web.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
HEALTHCHECK --interval=30s --timeout=3s --start-period=30s --retries=3 \
    CMD curl -f http://localhost:8080/health || exit 1
ENTRYPOINT ["dotnet", "FamOs.Web.dll"]
