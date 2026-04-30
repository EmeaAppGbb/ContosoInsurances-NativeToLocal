FROM mcr.microsoft.com/dotnet/nightly/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/nightly/sdk:10.0 AS build
WORKDIR /src

COPY src/ContosoInsurance.Data/ContosoInsurance.Data.csproj src/ContosoInsurance.Data/
COPY src/ContosoInsurance.ServiceDefaults/ContosoInsurance.ServiceDefaults.csproj src/ContosoInsurance.ServiceDefaults/
COPY src/ContosoInsurance.Api/ContosoInsurance.Api.csproj src/ContosoInsurance.Api/
RUN dotnet restore src/ContosoInsurance.Api/ContosoInsurance.Api.csproj

COPY src/ContosoInsurance.Data/ src/ContosoInsurance.Data/
COPY src/ContosoInsurance.ServiceDefaults/ src/ContosoInsurance.ServiceDefaults/
COPY src/ContosoInsurance.Api/ src/ContosoInsurance.Api/
RUN dotnet publish src/ContosoInsurance.Api/ContosoInsurance.Api.csproj -c Release -o /app/publish --no-restore

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "ContosoInsurance.Api.dll"]
