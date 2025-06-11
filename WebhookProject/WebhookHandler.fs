module WebhookHandler

open Microsoft.AspNetCore.Http
open Giraffe
open System.Text.Json
open System.Net.Http
open System.Collections.Concurrent
open System.Threading.Tasks
open System

type PaymentPayload = {
    event: string
    transaction_id: string
    amount: string
    currency: string
    timestamp: string
}

let tokenEsperado = "meu-token-secreto"
let transacoesRecebidas = ConcurrentDictionary<string, bool>()

let postToEndpoint endpoint payload =
    task {
        try
            use client = new HttpClient()
            client.Timeout <- TimeSpan.FromSeconds(10.0)
            let json = JsonSerializer.Serialize(payload)
            use content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            let! response = client.PostAsync($"http://localhost:5001/{endpoint}", content)
            return response.IsSuccessStatusCode
        with ex ->
            eprintfn $"Error calling {endpoint}: {ex.Message}"
            return false
    }

let confirmarTransacao = postToEndpoint "confirmar"
let cancelarTransacao = postToEndpoint "cancelar"

let webhookHandler: HttpHandler =
    fun next ctx -> task {
        let token = ctx.Request.Headers.["X-Webhook-Token"].ToString()

        if token <> tokenEsperado then
            return! RequestErrors.UNAUTHORIZED "Bearer" "Webhook" "Token inválido" next ctx
        else
            try
                let! payload = ctx.BindJsonAsync<PaymentPayload>()

                let amountParsed, amountValue = Double.TryParse(payload.amount)

                if String.IsNullOrWhiteSpace(payload.transaction_id) then
                    return! RequestErrors.BAD_REQUEST (text "transaction_id obrigatório") next ctx

                elif transacoesRecebidas.ContainsKey(payload.transaction_id) then
                    return! RequestErrors.BAD_REQUEST (text "Transação duplicada") next ctx

                elif payload.event <> "payment_success" then
                    let! success = cancelarTransacao payload
                    if not success then eprintfn "Failed to cancel transaction"
                    return! RequestErrors.BAD_REQUEST (text "Evento inválido") next ctx

                elif not amountParsed || amountValue <= 0.0 || String.IsNullOrWhiteSpace(payload.currency) then
                    let! success = cancelarTransacao payload
                    if not success then eprintfn "Failed to cancel transaction"
                    return! RequestErrors.BAD_REQUEST (text "Dados inválidos") next ctx

                elif String.IsNullOrWhiteSpace(payload.timestamp) then
                    let! success = cancelarTransacao payload
                    if not success then eprintfn "Failed to cancel transaction"
                    return! RequestErrors.BAD_REQUEST (text "Timestamp ausente") next ctx

                else
                    transacoesRecebidas.TryAdd(payload.transaction_id, true) |> ignore
                    let! success = confirmarTransacao payload
                    if not success then eprintfn "Failed to confirm transaction"
                    return! Successful.OK (text "Transação confirmada") next ctx
            with ex ->
                eprintfn $"Error processing request: {ex.Message}"
                return! RequestErrors.BAD_REQUEST (text (sprintf "Erro ao processar payload: %s" ex.Message)) next ctx
    }
