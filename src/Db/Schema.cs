namespace TapAndEat.Api.Db;

/// <summary>
/// The whole schema, created idempotently on startup (CREATE TABLE IF NOT
/// EXISTS — safe to run against a database that already has data). There's
/// no separate migration history table yet: for a project this size, "the
/// schema is the migration" is good enough, and every CREATE/ALTER here is
/// additive and backward compatible. If columns need to change later, add a
/// numbered migrations list and a schema_version table rather than editing
/// these statements in place.
/// </summary>
public static class Schema
{
    public static async Task EnsureCreatedAsync(Database db)
    {
        var statements = new[]
        {
            // ---- Sprint 1 ----
            """
            CREATE TABLE IF NOT EXISTS users (
                id TEXT PRIMARY KEY,
                full_name TEXT NOT NULL,
                email TEXT NOT NULL,
                password_hash TEXT NOT NULL,
                role TEXT NOT NULL,
                is_active INTEGER NOT NULL DEFAULT 1,
                security_stamp INTEGER NOT NULL DEFAULT 0,
                created_at INTEGER NOT NULL
            )
            """,
            "CREATE UNIQUE INDEX IF NOT EXISTS ix_users_email ON users (email COLLATE NOCASE)",

            """
            CREATE TABLE IF NOT EXISTS password_reset_tokens (
                id TEXT PRIMARY KEY,
                user_id TEXT NOT NULL REFERENCES users(id),
                token_hash TEXT NOT NULL,
                expires_at INTEGER NOT NULL,
                used INTEGER NOT NULL DEFAULT 0
            )
            """,
            "CREATE INDEX IF NOT EXISTS ix_password_reset_tokens_user ON password_reset_tokens (user_id)",

            """
            CREATE TABLE IF NOT EXISTS menu_items (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                description TEXT NOT NULL DEFAULT '',
                price TEXT NOT NULL,
                category TEXT NOT NULL DEFAULT 'General',
                dietary_tags TEXT NOT NULL DEFAULT '',
                stock_count INTEGER NOT NULL DEFAULT 0,
                low_stock_threshold INTEGER NOT NULL DEFAULT 5,
                prep_time_minutes INTEGER NOT NULL DEFAULT 5,
                is_published INTEGER NOT NULL DEFAULT 0,
                is_override INTEGER NOT NULL DEFAULT 0,
                created_at INTEGER NOT NULL,
                updated_at INTEGER NOT NULL
            )
            """,

            // ---- Sprint 2: orders / payments / wallet / queue / RFID ----
            """
            CREATE TABLE IF NOT EXISTS orders (
                id TEXT PRIMARY KEY,
                user_id TEXT NOT NULL REFERENCES users(id),
                status TEXT NOT NULL,
                created_at INTEGER NOT NULL,
                expires_at INTEGER NOT NULL,
                paid_at INTEGER NULL,
                collected_at INTEGER NULL,
                payment_token TEXT NULL,
                paid_by_payment_id TEXT NULL
            )
            """,
            "CREATE INDEX IF NOT EXISTS ix_orders_user ON orders (user_id)",
            "CREATE INDEX IF NOT EXISTS ix_orders_status ON orders (status)",

            """
            CREATE TABLE IF NOT EXISTS order_lines (
                id TEXT PRIMARY KEY,
                order_id TEXT NOT NULL REFERENCES orders(id),
                menu_item_id TEXT NOT NULL,
                name TEXT NOT NULL,
                unit_price TEXT NOT NULL,
                quantity INTEGER NOT NULL,
                prep_time_minutes INTEGER NOT NULL,
                line_index INTEGER NOT NULL DEFAULT 0
            )
            """,
            "CREATE INDEX IF NOT EXISTS ix_order_lines_order ON order_lines (order_id)",

            """
            CREATE TABLE IF NOT EXISTS payments (
                id TEXT PRIMARY KEY,
                purpose TEXT NOT NULL,
                user_id TEXT NOT NULL REFERENCES users(id),
                order_id TEXT NULL,
                amount TEXT NOT NULL,
                method TEXT NOT NULL,
                status TEXT NOT NULL,
                idempotency_key TEXT NULL,
                gateway_payment_ref TEXT NULL,
                gateway_transaction_id TEXT NULL,
                redirect_url TEXT NULL,
                failure_reason TEXT NULL,
                refunded_to_wallet INTEGER NOT NULL DEFAULT 0,
                created_at INTEGER NOT NULL,
                completed_at INTEGER NULL
            )
            """,
            "CREATE UNIQUE INDEX IF NOT EXISTS ix_payments_user_idem ON payments (user_id, idempotency_key) WHERE idempotency_key IS NOT NULL",
            "CREATE INDEX IF NOT EXISTS ix_payments_order ON payments (order_id)",

            """
            CREATE TABLE IF NOT EXISTS wallets (
                id TEXT PRIMARY KEY,
                user_id TEXT NOT NULL,
                balance TEXT NOT NULL DEFAULT '0',
                rfid_card_uid TEXT NULL,
                created_at INTEGER NOT NULL
            )
            """,
            "CREATE UNIQUE INDEX IF NOT EXISTS ix_wallets_user ON wallets (user_id)",
            "CREATE UNIQUE INDEX IF NOT EXISTS ix_wallets_card ON wallets (rfid_card_uid) WHERE rfid_card_uid IS NOT NULL",

            """
            CREATE TABLE IF NOT EXISTS wallet_transactions (
                id TEXT PRIMARY KEY,
                wallet_id TEXT NOT NULL REFERENCES wallets(id),
                type TEXT NOT NULL,
                amount TEXT NOT NULL,
                balance_after TEXT NOT NULL,
                description TEXT NOT NULL,
                idempotency_key TEXT NOT NULL UNIQUE,
                created_at INTEGER NOT NULL
            )
            """,
            "CREATE INDEX IF NOT EXISTS ix_wallet_tx_wallet ON wallet_transactions (wallet_id)",

            """
            CREATE TABLE IF NOT EXISTS subsidy_schedules (
                id TEXT PRIMARY KEY,
                user_id TEXT NOT NULL,
                amount TEXT NOT NULL,
                frequency TEXT NOT NULL,
                is_active INTEGER NOT NULL DEFAULT 1,
                last_credited_period_key TEXT NULL
            )
            """,
            "CREATE UNIQUE INDEX IF NOT EXISTS ix_subsidy_user ON subsidy_schedules (user_id)",

            """
            CREATE TABLE IF NOT EXISTS queue_tokens (
                id TEXT PRIMARY KEY,
                order_id TEXT NOT NULL,
                day_key TEXT NOT NULL,
                token_number INTEGER NOT NULL,
                status TEXT NOT NULL,
                estimated_prep_minutes INTEGER NOT NULL,
                work_minutes INTEGER NOT NULL,
                own_minutes INTEGER NOT NULL,
                created_at INTEGER NOT NULL,
                started_at INTEGER NULL,
                ready_at INTEGER NULL,
                collected_at INTEGER NULL
            )
            """,
            "CREATE UNIQUE INDEX IF NOT EXISTS ix_queue_tokens_order ON queue_tokens (order_id)",
            "CREATE UNIQUE INDEX IF NOT EXISTS ix_queue_tokens_day_number ON queue_tokens (day_key, token_number)",

            """
            CREATE TABLE IF NOT EXISTS rfid_tap_events (
                id TEXT PRIMARY KEY,
                source TEXT NOT NULL,
                card_uid TEXT NOT NULL,
                tapped_at INTEGER NOT NULL,
                outcome TEXT NOT NULL,
                reason TEXT NOT NULL,
                message TEXT NOT NULL,
                user_id TEXT NULL,
                order_id TEXT NULL,
                queue_token_id TEXT NULL,
                token_number INTEGER NULL,
                staff_user_id TEXT NOT NULL
            )
            """,
            "CREATE INDEX IF NOT EXISTS ix_taps_time ON rfid_tap_events (tapped_at DESC)",

            // ---- Sprint 3: inventory & forecasting ----
            """
            CREATE TABLE IF NOT EXISTS ingredients (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                unit TEXT NOT NULL,
                stock_quantity TEXT NOT NULL DEFAULT '0',
                reorder_threshold TEXT NOT NULL DEFAULT '0',
                created_at INTEGER NOT NULL
            )
            """,
            "CREATE UNIQUE INDEX IF NOT EXISTS ix_ingredients_name ON ingredients (name COLLATE NOCASE)",

            """
            CREATE TABLE IF NOT EXISTS menu_item_ingredients (
                menu_item_id TEXT NOT NULL,
                ingredient_id TEXT NOT NULL REFERENCES ingredients(id),
                quantity_per_item TEXT NOT NULL,
                PRIMARY KEY (menu_item_id, ingredient_id)
            )
            """,

            """
            CREATE TABLE IF NOT EXISTS inventory_transactions (
                id TEXT PRIMARY KEY,
                ingredient_id TEXT NOT NULL REFERENCES ingredients(id),
                change_amount TEXT NOT NULL,
                balance_after TEXT NOT NULL,
                reason TEXT NOT NULL,
                reference_order_id TEXT NULL,
                created_at INTEGER NOT NULL
            )
            """,
            "CREATE INDEX IF NOT EXISTS ix_inventory_tx_ingredient ON inventory_transactions (ingredient_id)",

            """
            CREATE TABLE IF NOT EXISTS demand_forecasts (
                id TEXT PRIMARY KEY,
                menu_item_id TEXT NOT NULL,
                forecast_date TEXT NOT NULL,
                predicted_quantity TEXT NOT NULL,
                model TEXT NOT NULL,
                sample_days INTEGER NOT NULL,
                generated_at INTEGER NOT NULL
            )
            """,
            "CREATE UNIQUE INDEX IF NOT EXISTS ix_forecasts_item_date ON demand_forecasts (menu_item_id, forecast_date)"
        };

        foreach (var sql in statements)
        {
            await db.ExecuteAsync(sql);
        }
    }
}
