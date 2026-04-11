alter table if exists request_logs
    drop constraint if exists request_logs_session_id_fkey;

drop index if exists idx_request_logs_session_id;

alter table if exists request_logs
    drop column if exists session_id,
    drop column if exists is_partial,
    drop column if exists is_unsupported;

drop index if exists idx_sessions_participant_id;
drop table if exists sessions;

create table if not exists request_feedback (
    id bigserial primary key,
    participant_id uuid not null references participants(id) on delete cascade,
    request_id text not null,
    reaction text not null check (reaction in ('up', 'down')),
    created_at timestamptz not null default now(),
    unique (request_id, participant_id)
);

create index if not exists idx_request_feedback_participant_time
    on request_feedback (participant_id, created_at desc);

create index if not exists idx_request_feedback_request_id
    on request_feedback (request_id);
