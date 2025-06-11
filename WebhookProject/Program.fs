open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Giraffe
open WebhookHandler

/// Defines the web application's HTTP routes.
let webApp: HttpHandler =
    choose [
        // Route POST requests to "/webhook" to the webhookHandler.
        POST >=> route "/webhook" >=> webhookHandler
    ]

// Create a new web application builder.
let builder = WebApplication.CreateBuilder()
// Add Giraffe services to the dependency injection container.
builder.Services.AddGiraffe() |> ignore

// Build the web application.
let app = builder.Build()
// Use the Giraffe web application handler.
app.UseGiraffe webApp
// Run the application.
app.Urls.Add("http://localhost:5000")
app.Run()