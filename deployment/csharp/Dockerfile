FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY app/ ./
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "CargoHub.Api.dll"]
