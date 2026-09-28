FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore
RUN dotnet publish src/ClaimsApi.Api -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
# Jwt__Key and Auth__Users__* must be supplied at runtime; the app refuses to start without a signing key.
USER app
ENTRYPOINT ["dotnet", "ClaimsApi.Api.dll"]
