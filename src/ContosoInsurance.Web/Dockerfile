FROM mcr.microsoft.com/dotnet/nightly/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/nightly/sdk:10.0 AS build
WORKDIR /src

COPY nuget.config .
COPY src/ContosoInsurance.Data/ContosoInsurance.Data.csproj src/ContosoInsurance.Data/
COPY src/ContosoInsurance.ServiceDefaults/ContosoInsurance.ServiceDefaults.csproj src/ContosoInsurance.ServiceDefaults/
COPY src/ContosoInsurance.Web/ContosoInsurance.Web.csproj src/ContosoInsurance.Web/
RUN dotnet restore src/ContosoInsurance.Web/ContosoInsurance.Web.csproj

COPY src/ContosoInsurance.Data/ src/ContosoInsurance.Data/
COPY src/ContosoInsurance.ServiceDefaults/ src/ContosoInsurance.ServiceDefaults/
COPY src/ContosoInsurance.Web/ src/ContosoInsurance.Web/
RUN dotnet publish src/ContosoInsurance.Web/ContosoInsurance.Web.csproj -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "ContosoInsurance.Web.dll"]
