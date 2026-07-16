locals {
  name_prefix = var.name_prefix

  # Secret Manager secret ids (sanitized — no colons). The deploy tool syncs each
  # secret's value from secrets.<env>.json as a new version; Terraform only creates
  # the containers, so no secret material lands in state or the repo.
  secret_ids = toset([
    "ConnectionStrings-ChimpOut",
    "ConnectionStrings-Messaging",
    "ConnectionStrings-Redis",
    "GeneralSettings-Token-TokenOwnerPrivateKey",
    "GeneralSettings-LaunchTokens-MainSigningPrivateKeyPem",
    "GeneralSettings-SecretProtection-Key",
    "GeneralSettings-Fx-ApiKey",
  ])

  # ASP.NET env var name (double-underscore) -> Secret Manager secret id.
  secret_env = {
    "ConnectionStrings__ChimpOut"                             = "ConnectionStrings-ChimpOut"
    "ConnectionStrings__Messaging"                            = "ConnectionStrings-Messaging"
    "ConnectionStrings__Redis"                                = "ConnectionStrings-Redis"
    "GeneralSettings__Token__TokenOwnerPrivateKey"            = "GeneralSettings-Token-TokenOwnerPrivateKey"
    "GeneralSettings__LaunchTokens__MainSigningPrivateKeyPem" = "GeneralSettings-LaunchTokens-MainSigningPrivateKeyPem"
    "GeneralSettings__SecretProtection__Key"                  = "GeneralSettings-SecretProtection-Key"
    "GeneralSettings__Fx__ApiKey"                             = "GeneralSettings-Fx-ApiKey"
  }

  plain_env = merge({ ASPNETCORE_ENVIRONMENT = "Production" }, var.plain_env)
}

module "network" {
  source      = "./modules/network"
  project_id  = var.project_id
  region      = var.region
  name_prefix = local.name_prefix
}

module "registry" {
  source     = "./modules/registry"
  project_id = var.project_id
  region     = var.region
  labels     = var.labels
}

module "iam" {
  source      = "./modules/iam"
  project_id  = var.project_id
  name_prefix = local.name_prefix
}

module "data" {
  source                     = "./modules/data"
  project_id                 = var.project_id
  region                     = var.region
  name_prefix                = local.name_prefix
  network_id                 = module.network.network_id
  private_vpc_connection_id  = module.network.private_vpc_connection_id
  enable_deletion_protection = var.enable_deletion_protection
  db_tier                    = var.db_tier
  max_connections            = var.max_connections
  db_app_user                = var.db_app_user
  db_app_password            = var.db_app_password
  redis_tier                 = var.redis_tier
  redis_memory_size_gb       = var.redis_memory_size_gb
  labels                     = var.labels
}

module "secrets" {
  source     = "./modules/secrets"
  project_id = var.project_id
  secret_ids = local.secret_ids
  accessor_members = {
    for id in local.secret_ids : id => [
      "serviceAccount:${module.iam.api_sa_email}",
      "serviceAccount:${module.iam.consumers_sa_email}",
    ]
  }
  labels = var.labels
}

# Consumers first — it owns EF migrations/seeding; the deploy tool also releases it
# before the API. Depends on secrets + data existing.
module "cloud_run_consumers" {
  source                = "./modules/cloud_run_consumers"
  project_id            = var.project_id
  region                = var.region
  name_prefix           = local.name_prefix
  image                 = var.consumers_image
  service_account_email = module.iam.consumers_sa_email
  vpc_connector_id      = module.network.connector_id
  consumer_count        = var.consumer_count
  plain_env             = local.plain_env
  secret_env            = local.secret_env

  depends_on = [module.secrets, module.data]
}

module "cloud_run_api" {
  source                = "./modules/cloud_run_api"
  project_id            = var.project_id
  region                = var.region
  name_prefix           = local.name_prefix
  image                 = var.api_image
  service_account_email = module.iam.api_sa_email
  vpc_connector_id      = module.network.connector_id
  min_instances         = var.api_min_instances
  max_instances         = var.api_max_instances
  plain_env             = local.plain_env
  secret_env            = local.secret_env

  depends_on = [module.secrets, module.data, module.cloud_run_consumers]
}

module "front_site" {
  source                  = "./modules/static_site"
  project_id              = var.project_id
  name_prefix             = "${local.name_prefix}-front"
  bucket_name             = var.front_bucket_name
  location                = var.bucket_location
  custom_response_headers = ["Content-Security-Policy: frame-ancestors *"]
  labels                  = var.labels
}

module "lp_site" {
  source      = "./modules/static_site"
  project_id  = var.project_id
  name_prefix = "${local.name_prefix}-lp"
  bucket_name = var.lp_bucket_name
  location    = var.bucket_location
  labels      = var.labels
}

module "edge" {
  source                = "./modules/edge"
  project_id            = var.project_id
  region                = var.region
  name_prefix           = local.name_prefix
  api_service_name      = module.cloud_run_api.service_name
  app_backend_bucket_id = module.front_site.backend_bucket_id
  lp_backend_bucket_id  = module.lp_site.backend_bucket_id
  dns_zone_name         = var.dns_zone_name
  domain_api            = var.domain_api
  domain_app            = var.domain_app
  domain_lp             = var.domain_lp
  domain_lp_www         = var.domain_lp_www
}
