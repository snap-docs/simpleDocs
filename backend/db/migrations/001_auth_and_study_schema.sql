create extension if not exists pgcrypto;

create table if not exists participants (
    id uuid primary key default gen_random_uuid(),
    created_at timestamptz not null default now()
);

create table if not exists redeem_codes (
    code text primary key,
    participant_id uuid references participants(id) on delete set null,
    is_used boolean not null default false,
    issued_at timestamptz not null default now(),
    used_at timestamptz null
);

create index if not exists idx_redeem_codes_participant_id
    on redeem_codes (participant_id);

create table if not exists refresh_tokens (
    id uuid primary key default gen_random_uuid(),
    participant_id uuid not null references participants(id) on delete cascade,
    token_hash text not null unique,
    expires_at timestamptz not null,
    revoked_at timestamptz null,
    created_at timestamptz not null default now()
);

create index if not exists idx_refresh_tokens_participant_id
    on refresh_tokens (participant_id);

create index if not exists idx_refresh_tokens_active
    on refresh_tokens (token_hash, revoked_at, expires_at);

create table if not exists sessions (
    session_id text primary key,
    participant_id uuid not null references participants(id) on delete cascade,
    created_at timestamptz not null default now(),
    last_seen_at timestamptz not null default now()
);

create index if not exists idx_sessions_participant_id
    on sessions (participant_id);

create table if not exists request_logs (
    id bigserial primary key,
    participant_id uuid not null references participants(id) on delete cascade,
    session_id text not null references sessions(session_id) on delete cascade,
    request_id text not null unique,
    timestamp timestamptz not null default now(),
    usage_context text not null,
    window_title text not null default '',
    selected_method text not null default 'unknown',
    background_method text not null default 'unknown',
    is_partial boolean not null default false,
    is_unsupported boolean not null default false,
    task_type text not null,
    time_to_first_token_ms integer null,
    total_response_time_ms integer not null,
    selected_text text not null,
    response_text text not null
);

create index if not exists idx_request_logs_participant_time
    on request_logs (participant_id, timestamp desc);

create index if not exists idx_request_logs_session_id
    on request_logs (session_id);

create index if not exists idx_request_logs_task_type
    on request_logs (task_type);
