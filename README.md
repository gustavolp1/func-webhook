# Webhook Receiver

This project implements a secure and robust webhook receiver in **F#** using **Giraffe** and **.NET 6**.

The application exposes a `/webhook` endpoint to receive **payment notifications** in JSON format. It performs multiple validations and conditionally confirms or cancels the transaction via HTTP requests to secondary endpoints.

## Features

- Receives **POST** requests at `/webhook`
- Parses and validates **JSON payloads**
- Confirms valid transactions via `POST /confirmar`
- Cancels invalid transactions via `POST /cancelar`
- Handles **duplicate transactions**
- Stores transactions in an **SQLite database** (via Entity Framework Core)
- Includes full error handling and meaningful HTTP responses
- Successfully passes all automated test cases

## How to Run

### 1. Prerequisites

- [.NET 6 SDK](https://dotnet.microsoft.com/download/dotnet/6.0)
- [Python](https://www.python.org/downloads/)
- `pip install requests` (for the test script)

### 2. Running the Server

```bash
cd WebhookProject
dotnet run
```

### 3. Running the Tests

```bash
python test_webhook.py
```

You'll see output for all test cases and whether each one passed.

## Notes

- The webhook accepts JSON data with fields: `event`, `transaction_id`, `amount`, `currency`, and `timestamp`.
- Transactions with invalid data or duplicate `transaction_id`s are rejected.
- Successfully received and validated transactions are stored in an SQLite database and also forwarded to a `/confirmar` endpoint.
- Failed ones are forwarded to `/cancelar`.