-- =============================================================================
-- Supabase migration: shared_servers table
-- Enables multiple users to access the same server configurations.
--
-- Run this in your Supabase SQL Editor (Dashboard > SQL Editor > New query).
-- =============================================================================

-- 1. Create the shared_servers table
CREATE TABLE IF NOT EXISTS public.shared_servers (
    id              uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    name            text NOT NULL,
    address         text NOT NULL,
    port            int  NOT NULL DEFAULT 443,
    protocol        text NOT NULL DEFAULT 'V2Ray',

    -- V2Ray (VMess) settings
    v2ray_user_id   text DEFAULT '',
    v2ray_alter_id  int  DEFAULT 0,
    v2ray_security  text DEFAULT 'auto',
    v2ray_transport text DEFAULT 'WebSocket',
    v2ray_path      text DEFAULT '/ws',
    v2ray_host      text DEFAULT '',
    v2ray_tls       bool DEFAULT true,
    v2ray_sni       text DEFAULT '',

    -- Shadowsocks settings
    ss_password         text DEFAULT '',
    ss_encryption       text DEFAULT 'Aes256Gcm',
    ss_plugin           text DEFAULT '',
    ss_plugin_options   text DEFAULT '',

    -- Metadata
    remarks         text DEFAULT '',
    created_by      uuid REFERENCES auth.users(id),
    created_at      timestamptz DEFAULT now(),
    enabled         bool DEFAULT true
);

-- 2. Enable Row Level Security
ALTER TABLE public.shared_servers ENABLE ROW LEVEL SECURITY;

-- 3. Policy: All authenticated users with gateway.access can READ shared servers
CREATE POLICY "Users with gateway.access can read shared servers"
    ON public.shared_servers
    FOR SELECT
    TO authenticated
    USING (
        EXISTS (
            SELECT 1 FROM public.profiles
            WHERE profiles.id = auth.uid()
            AND profiles.permissions @> '["gateway.access"]'::jsonb
        )
    );

-- 4. Policy: Only users with gateway.admin permission can INSERT/UPDATE/DELETE
CREATE POLICY "Admins can manage shared servers"
    ON public.shared_servers
    FOR ALL
    TO authenticated
    USING (
        EXISTS (
            SELECT 1 FROM public.profiles
            WHERE profiles.id = auth.uid()
            AND profiles.permissions @> '["gateway.admin"]'::jsonb
        )
    )
    WITH CHECK (
        EXISTS (
            SELECT 1 FROM public.profiles
            WHERE profiles.id = auth.uid()
            AND profiles.permissions @> '["gateway.admin"]'::jsonb
        )
    );

-- 5. Create index for fast lookup of enabled servers
CREATE INDEX IF NOT EXISTS idx_shared_servers_enabled
    ON public.shared_servers (enabled)
    WHERE enabled = true;

-- =============================================================================
-- USAGE NOTES:
--
-- To grant a user admin access (can publish/unpublish shared servers):
--   UPDATE profiles
--   SET permissions = permissions || '["gateway.admin"]'::jsonb
--   WHERE id = '<user-uuid>';
--
-- To add a shared server manually via SQL:
--   INSERT INTO shared_servers (name, address, port, protocol, v2ray_user_id, v2ray_transport, v2ray_tls)
--   VALUES ('Tokyo V2Ray', '203.0.113.50', 443, 'V2Ray', 'your-uuid-here', 'WebSocket', true);
--
-- Multiple users can connect to the same server simultaneously because:
--   - Each client runs its own local v2ray-core process
--   - V2Ray/Shadowsocks servers natively support multiple concurrent clients
--   - Local proxy ports (10808/10809) are per-machine, no conflict between users
-- =============================================================================
