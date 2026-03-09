FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["src/FortressFormTools.Web/FortressFormTools.Web.csproj", "FortressFormTools.Web/"]
COPY ["src/FortressFormTools.Data/FortressFormTools.Data.csproj", "FortressFormTools.Data/"]
RUN dotnet restore "FortressFormTools.Web/FortressFormTools.Web.csproj"
COPY src/ .
WORKDIR "/src/FortressFormTools.Web"
RUN dotnet build "FortressFormTools.Web.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "FortressFormTools.Web.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "FortressFormTools.Web.dll"]
