ALTER TABLE request_logs
  ADD COLUMN IF NOT EXISTS capture_method_extended VARCHAR(40);

ALTER TABLE request_logs
  ADD COLUMN IF NOT EXISTS vision_layers_used VARCHAR(50);

ALTER TABLE request_logs
  ADD COLUMN IF NOT EXISTS total_image_bytes_sent INT;
