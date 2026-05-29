FROM mcr.microsoft.com/dotnet/nightly/aspnet:10.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/nightly/sdk:10.0 AS build
WORKDIR /src

COPY nuget.config .
COPY src/ContosoInsurance.Data/ContosoInsurance.Data.csproj src/ContosoInsurance.Data/
COPY src/ContosoInsurance.ServiceDefaults/ContosoInsurance.ServiceDefaults.csproj src/ContosoInsurance.ServiceDefaults/
COPY src/ContosoInsurance.Worker/ContosoInsurance.Worker.csproj src/ContosoInsurance.Worker/
RUN dotnet restore src/ContosoInsurance.Worker/ContosoInsurance.Worker.csproj

COPY src/ContosoInsurance.Data/ src/ContosoInsurance.Data/
COPY src/ContosoInsurance.ServiceDefaults/ src/ContosoInsurance.ServiceDefaults/
COPY src/ContosoInsurance.Worker/ src/ContosoInsurance.Worker/
RUN dotnet publish src/ContosoInsurance.Worker/ContosoInsurance.Worker.csproj -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "ContosoInsurance.Worker.dll"]
