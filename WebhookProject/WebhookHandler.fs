module WebhookHandler

open Microsoft.AspNetCore.Http
open Giraffe
open System.Text.Json
open System.Net.Http
open System.Collections.Concurrent
open System.Threading.Tasks
open System // Required for System.Text.Encoding

/// Represents the structure of the payment payload received from the webhook.
type PaymentPayload = {
    event: string
    transaction_id: string
    amount: float
    currency: string
    timestamp: string
}

/// The expected secret token for webhook validation.
let tokenEsperado = "meu-token-secreto"

/// A concurrent dictionary to store received transaction IDs to prevent duplicates.
let transacoesRecebidas = ConcurrentDictionary<string, bool>()

/// Confirms a transaction by sending a POST request to a local endpoint.
let confirmarTransacao (payload: PaymentPayload) =
    task {
        // Use a new HttpClient instance for each request to avoid connection pooling issues.
        use client = new HttpClient()
        // Serialize the payload to JSON.
        let json = JsonSerializer.Serialize(payload)
        // Create StringContent with JSON, UTF8 encoding, and application/json media type.
        use content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        // Send the POST request and await the response.
        let! _ = client.PostAsync("http://localhost:5001/confirmar", content)
        return () // Return unit, as this function doesn't return any specific value.
    }

/// Cancels a transaction by sending a POST request to a local endpoint.
let cancelarTransacao (payload: PaymentPayload) =
    task {
        // Use a new HttpClient instance for each request.
        use client = new HttpClient()
        // Serialize the payload to JSON.
        let json = JsonSerializer.Serialize(payload)
        // Create StringContent with JSON, UTF8 encoding, and application/json media type.
        use content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        // Send the POST request and await the response.
        let! _ = client.PostAsync("http://localhost:5001/cancelar", content)
        return () // Return unit, as this function doesn't return any specific value.
    }

/// The main webhook HTTP handler.
/// It processes incoming webhook requests, validates the token and payload,
/// and then either confirms or cancels the transaction.
let webhookHandler: HttpHandler =
    fun next ctx -> task { // The task block here should ultimately return a HttpContext option (Task<HttpContext option>).
        // Retrieve the X-Webhook-Token header.
        let token = ctx.Request.Headers.["X-Webhook-Token"].ToString()

        // Validate the webhook token.
        if token <> tokenEsperado then
            // If the token is invalid, execute the Giraffe handler and return its result.
            // By doing 'let! result = ...; return result', we explicitly yield the HttpContext option
            // from the Task<HttpContext option> produced by Successful.OK.
            let! result = Successful.OK (text "Token inválido (ignorado)") next ctx
            return result
        else
            // Attempt to bind the request body to a PaymentPayload.
            // Using a Result type to propagate parsing errors.
            let! payloadResult =
                task { // This inner task returns Result<PaymentPayload, string>
                    try
                        // If successful, wrap the payload in Ok and return as a Task.
                        let! p = ctx.BindJsonAsync<PaymentPayload>()
                        return Ok p
                    with ex ->
                        // If an exception occurs, wrap the error message in Error and return as a Task.
                        return Error $"Erro ao desserializar payload: {ex.Message}"
                }

            match payloadResult with
            | Ok payload ->
                // Now, ensure this entire block consistently returns HttpContext option
                if System.String.IsNullOrWhiteSpace(payload.transaction_id) then
                    let! result = RequestErrors.badRequest (text "transaction_id obrigatório") next ctx
                    return result
                else if transacoesRecebidas.ContainsKey(payload.transaction_id) then
                    let! result = RequestErrors.badRequest (text "Transação duplicada") next ctx
                    return result
                else if payload.amount <= 0.0 || payload.currency <> "BRL" then
                    do! cancelarTransacao payload // Side effect: await cancellation
                    let! result = RequestErrors.badRequest (text "Dados inválidos") next ctx
                    return result
                else if System.String.IsNullOrWhiteSpace(payload.timestamp) then
                    do! cancelarTransacao payload // Side effect: await cancellation
                    let! result = RequestErrors.badRequest (text "Timestamp ausente") next ctx
                    return result
                else
                    // This is the "happy path" if all conditions above are false.
                    // It must also explicitly return a HttpContext option.
                    transacoesRecebidas.TryAdd(payload.transaction_id, true) |> ignore // Add transaction ID to prevent duplicates.
                    do! confirmarTransacao payload // Side effect: await confirmation

                    // Return a successful OK response for the happy path.
                    let! result = Successful.OK (text "Transação confirmada") next ctx
                    return result

            | Error errorMessage ->
                // If payload parsing failed, return a bad request with the captured error message.
                let! result = RequestErrors.badRequest (text errorMessage) next ctx
                return result
    }