FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["src/FortressPortal.Web/FortressPortal.Web.csproj", "FortressPortal.Web/"]
RUN dotnet restore "FortressPortal.Web/FortressPortal.Web.csproj"
COPY src/ .
WORKDIR "/src/FortressPortal.Web"
RUN dotnet build "FortressPortal.Web.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "FortressPortal.Web.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "FortressPortal.Web.dll"]
