output "instance_connection_name" {
  description = "Cloud SQL connection name (project:region:instance) for the Auth Proxy."
  value       = google_sql_database_instance.postgres.connection_name
}

output "instance_name" {
  value = google_sql_database_instance.postgres.name
}

output "private_ip" {
  value = google_sql_database_instance.postgres.private_ip_address
}

output "game_database" {
  value = google_sql_database.game.name
}

output "messaging_database" {
  value = google_sql_database.messaging.name
}

output "redis_host" {
  value = google_redis_instance.cache.host
}

output "redis_port" {
  value = google_redis_instance.cache.port
}
