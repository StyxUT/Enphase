# Enphase Local API

This is a local API for interacting with Enphase Envoy solar inverters. It provides endpoints to retrieve power production and consumption data.

## Features

- Net power production calculation
- Power production data
- Power consumption data
- Real-time updates with auto-refresh

## Endpoints

- `/netpowerproduction` - Displays net power production with current values and gradients
- `/production` - Returns production data
- `/consumption` - Returns consumption data
- `/healthcheck` - Health check endpoint

## Building and Running

To build and run the API:

```bash
dotnet build Enphase.sln
dotnet run --project EnphaseLocal
```

## Development

This project uses .NET 9.0 with the following conventions:
- File-scoped namespaces
- Async/await patterns
- Dependency injection
- System.Text.Json for serialization
- xUnit for testing

## Contributing

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/your-feature`)
3. Make your changes
4. Commit your changes (`git commit -am 'Add some feature'`)
5. Push to the branch (`git push origin feature/your-feature`)
6. Create a new Pull Request