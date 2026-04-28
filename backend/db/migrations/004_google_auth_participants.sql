alter table if exists participants
    add column if not exists auth_provider text not null default 'legacy_redeem_code',
    add column if not exists google_sub text null,
    add column if not exists email text null,
    add column if not exists email_verified boolean not null default false,
    add column if not exists display_name text null,
    add column if not exists given_name text null,
    add column if not exists family_name text null,
    add column if not exists avatar_url text null,
    add column if not exists last_login_at timestamptz null;

create unique index if not exists idx_participants_google_sub
    on participants (google_sub)
    where google_sub is not null;

create index if not exists idx_participants_auth_provider
    on participants (auth_provider);

create index if not exists idx_participants_email_lower
    on participants (lower(email))
    where email is not null;
