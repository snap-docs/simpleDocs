create table if not exists auth_provider_links (
    id uuid primary key default gen_random_uuid(),
    participant_id uuid not null references participants(id) on delete cascade,
    provider_type text not null
        check (provider_type in ('redeem_code', 'google', 'password')),
    provider_subject text not null,
    provider_email text null,
    password_hash text null,
    password_algorithm text null,
    profile_json jsonb not null default '{}'::jsonb,
    metadata_json jsonb not null default '{}'::jsonb,
    is_enabled boolean not null default true,
    created_at timestamptz not null default now(),
    linked_at timestamptz not null default now(),
    last_authenticated_at timestamptz null,
    last_verified_at timestamptz null,
    constraint auth_provider_links_password_fields_check
        check (
            (provider_type = 'password' and password_hash is not null and password_algorithm is not null)
            or
            (provider_type <> 'password' and password_hash is null and password_algorithm is null)
        )
);

create unique index if not exists idx_auth_provider_links_unique_subject
    on auth_provider_links (provider_type, provider_subject);

create index if not exists idx_auth_provider_links_participant
    on auth_provider_links (participant_id, provider_type);

create index if not exists idx_auth_provider_links_email
    on auth_provider_links (lower(provider_email))
    where provider_email is not null;

create table if not exists auth_sessions (
    id uuid primary key default gen_random_uuid(),
    participant_id uuid not null references participants(id) on delete cascade,
    auth_provider_link_id uuid null references auth_provider_links(id) on delete set null,
    auth_provider_type text not null
        check (auth_provider_type in ('redeem_code', 'google', 'password')),
    client_kind text not null default 'desktop',
    client_label text null,
    metadata_json jsonb not null default '{}'::jsonb,
    status text not null default 'active'
        check (status in ('active', 'revoked', 'expired')),
    created_at timestamptz not null default now(),
    last_seen_at timestamptz null,
    revoked_at timestamptz null
);

create index if not exists idx_auth_sessions_participant
    on auth_sessions (participant_id, status, created_at desc);

create index if not exists idx_auth_sessions_provider_link
    on auth_sessions (auth_provider_link_id);

do $$
begin
    if exists (
        select 1
        from information_schema.columns
        where table_schema = 'public'
          and table_name = 'participants'
          and column_name = 'last_login_at'
    ) and not exists (
        select 1
        from information_schema.columns
        where table_schema = 'public'
          and table_name = 'participants'
          and column_name = 'last_authenticated_at'
    ) then
        alter table participants
            rename column last_login_at to last_authenticated_at;
    end if;
end $$;

alter table if exists participants
    add column if not exists email text null,
    add column if not exists email_verified boolean not null default false,
    add column if not exists display_name text null,
    add column if not exists given_name text null,
    add column if not exists family_name text null,
    add column if not exists avatar_url text null,
    add column if not exists last_authenticated_at timestamptz null;

create index if not exists idx_participants_email_lower_current
    on participants (lower(email))
    where email is not null;

alter table if exists redeem_codes
    add column if not exists auth_provider_link_id uuid null references auth_provider_links(id) on delete set null;

alter table if exists refresh_tokens
    add column if not exists auth_session_id uuid null references auth_sessions(id) on delete cascade,
    add column if not exists auth_provider_link_id uuid null references auth_provider_links(id) on delete set null,
    add column if not exists last_used_at timestamptz null;

create index if not exists idx_refresh_tokens_session
    on refresh_tokens (auth_session_id);

create index if not exists idx_refresh_tokens_provider_link
    on refresh_tokens (auth_provider_link_id);

insert into auth_provider_links (
    participant_id,
    provider_type,
    provider_subject,
    provider_email,
    profile_json,
    metadata_json,
    created_at,
    linked_at,
    last_authenticated_at,
    last_verified_at
)
select
    p.id,
    'google',
    trim(p.google_sub),
    case
        when p.email is null or trim(p.email) = '' then null
        else lower(trim(p.email))
    end,
    jsonb_strip_nulls(
        jsonb_build_object(
            'display_name', nullif(trim(p.display_name), ''),
            'given_name', nullif(trim(p.given_name), ''),
            'family_name', nullif(trim(p.family_name), ''),
            'avatar_url', nullif(trim(p.avatar_url), '')
        )
    ),
    '{}'::jsonb,
    p.created_at,
    coalesce(p.last_authenticated_at, p.created_at),
    p.last_authenticated_at,
    case when p.email_verified then p.last_authenticated_at else null end
from participants p
where p.google_sub is not null
  and trim(p.google_sub) <> ''
on conflict (provider_type, provider_subject) do update
set
    provider_email = excluded.provider_email,
    profile_json = excluded.profile_json,
    last_authenticated_at = coalesce(excluded.last_authenticated_at, auth_provider_links.last_authenticated_at),
    last_verified_at = coalesce(excluded.last_verified_at, auth_provider_links.last_verified_at);

insert into auth_provider_links (
    participant_id,
    provider_type,
    provider_subject,
    profile_json,
    metadata_json,
    created_at,
    linked_at,
    last_authenticated_at
)
select
    rc.participant_id,
    'redeem_code',
    upper(trim(rc.code)),
    '{}'::jsonb,
    jsonb_strip_nulls(
        jsonb_build_object(
            'redeem_code', rc.code,
            'issued_at', rc.issued_at,
            'used_at', rc.used_at
        )
    ),
    coalesce(rc.issued_at, now()),
    coalesce(rc.used_at, rc.issued_at, now()),
    rc.used_at
from redeem_codes rc
where rc.participant_id is not null
  and trim(rc.code) <> ''
on conflict (provider_type, provider_subject) do update
set
    participant_id = excluded.participant_id,
    metadata_json = excluded.metadata_json,
    last_authenticated_at = coalesce(excluded.last_authenticated_at, auth_provider_links.last_authenticated_at);

update redeem_codes as rc
set auth_provider_link_id = apl.id
from auth_provider_links as apl
where rc.participant_id = apl.participant_id
  and apl.provider_type = 'redeem_code'
  and apl.provider_subject = upper(trim(rc.code))
  and rc.auth_provider_link_id is null;

update participants
set last_authenticated_at = coalesce(last_authenticated_at, created_at)
where last_authenticated_at is null
  and exists (
      select 1
      from auth_provider_links apl
      where apl.participant_id = participants.id
  );

with prepared as (
    select
        rt.id as refresh_token_id,
        gen_random_uuid() as session_id,
        rt.participant_id,
        coalesce(
            rt.auth_provider_link_id,
            redeem_link.id,
            google_link.id,
            any_link.id
        ) as auth_provider_link_id,
        coalesce(
            redeem_link.provider_type,
            google_link.provider_type,
            any_link.provider_type,
            'redeem_code'
        ) as auth_provider_type,
        rt.created_at,
        rt.last_used_at,
        rt.revoked_at,
        rt.expires_at
    from refresh_tokens rt
    left join lateral (
        select id, provider_type
        from auth_provider_links
        where participant_id = rt.participant_id
          and provider_type = 'redeem_code'
        order by linked_at asc
        limit 1
    ) as redeem_link on true
    left join lateral (
        select id, provider_type
        from auth_provider_links
        where participant_id = rt.participant_id
          and provider_type = 'google'
        order by linked_at asc
        limit 1
    ) as google_link on true
    left join lateral (
        select id, provider_type
        from auth_provider_links
        where participant_id = rt.participant_id
        order by linked_at asc
        limit 1
    ) as any_link on true
    where rt.auth_session_id is null
),
inserted as (
    insert into auth_sessions (
        id,
        participant_id,
        auth_provider_link_id,
        auth_provider_type,
        client_kind,
        client_label,
        metadata_json,
        status,
        created_at,
        last_seen_at,
        revoked_at
    )
    select
        prepared.session_id,
        prepared.participant_id,
        prepared.auth_provider_link_id,
        prepared.auth_provider_type,
        'desktop',
        'legacy-migrated-session',
        jsonb_build_object('migrated_from_refresh_token', prepared.refresh_token_id),
        case
            when prepared.revoked_at is not null then 'revoked'
            when prepared.expires_at <= now() then 'expired'
            else 'active'
        end,
        prepared.created_at,
        coalesce(prepared.last_used_at, prepared.created_at),
        prepared.revoked_at
    from prepared
)
update refresh_tokens as rt
set
    auth_session_id = prepared.session_id,
    auth_provider_link_id = prepared.auth_provider_link_id,
    last_used_at = coalesce(rt.last_used_at, rt.created_at)
from prepared
where rt.id = prepared.refresh_token_id;

drop index if exists idx_participants_google_sub;
drop index if exists idx_participants_auth_provider;

alter table if exists participants
    drop column if exists auth_provider,
    drop column if exists google_sub;
