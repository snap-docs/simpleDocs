alter table if exists request_logs
    add column if not exists environment_type text not null default 'unknown',
    add column if not exists process_name text not null default '',
    add column if not exists background_context text not null default '',
    add column if not exists status text not null default 'completed';

create index if not exists idx_request_logs_environment_type
    on request_logs (environment_type);

create index if not exists idx_request_logs_process_name
    on request_logs (process_name);

create index if not exists idx_request_logs_status
    on request_logs (status);
