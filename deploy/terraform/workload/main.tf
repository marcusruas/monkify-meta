locals {
  # Environment-scoped: spacestroker-dev / spacestroker-prod, supplied by the deploy tool.
  # Development and production share one GCP project, so every project-global name carries it.
  name_prefix = var.name_prefix

  # Secret Manager secret ids (sanitized — no colons). The deploy tool syncs each
  # secret's value from secrets.<env>.json as a new version; Terraform only creates
  # the containers, so no secret material lands in state or the repo.
  # Ids are project-global, so they are env-prefixed like everything else.
  base_secret_ids = [
    "ConnectionStrings-SpaceStroker",
    "ConnectionStrings-Messaging",
    "ConnectionStrings-Redis",
    "GeneralSettings-Token-TokenOwnerPrivateKey",
    "GeneralSettings-LaunchTokens-MainSigningPrivateKeyPem",
    "GeneralSettings-SecretProtection-Key",
    "GeneralSettings-Fx-ApiKey",
    # The RPC endpoint carries the provider's api-key in its query string, so the whole URL is a secret.
    "GeneralSettings-Token-ClusterUrl",
  ]

  secret_ids = toset([for id in local.base_secret_ids : "${local.name_prefix}-${id}"])

  # ASP.NET env var name (double-underscore) -> Secret Manager secret id.
  # The env var NAMES are app config and never change; only the ids they point at are env-scoped.
  base_secret_env = {
    "ConnectionStrings__SpaceStroker"                         = "ConnectionStrings-SpaceStroker"
    "ConnectionStrings__Messaging"                            = "ConnectionStrings-Messaging"
    "ConnectionStrings__Redis"                                = "ConnectionStrings-Redis"
    "GeneralSettings__Token__TokenOwnerPrivateKey"            = "GeneralSettings-Token-TokenOwnerPrivateKey"
    "GeneralSettings__LaunchTokens__MainSigningPrivateKeyPem" = "GeneralSettings-LaunchTokens-MainSigningPrivateKeyPem"
    "GeneralSettings__SecretProtection__Key"                  = "GeneralSettings-SecretProtection-Key"
    "GeneralSettings__Fx__ApiKey"                             = "GeneralSettings-Fx-ApiKey"
    "GeneralSettings__Token__ClusterUrl"                      = "GeneralSettings-Token-ClusterUrl"
  }

  secret_env = { for env_name, id in local.base_secret_env : env_name => "${local.name_prefix}-${id}" }

  # Both hosts run the Production ASP.NET environment in Cloud Run even for the DEVELOPMENT GCP
  # environment — it is a deployed environment, not a developer machine. Override via appConfig.
  plain_env = merge({ ASPNETCORE_ENVIRONMENT = "Production" }, var.plain_env)
}

module "network" {
  source      = "./modules/network"
  project_id  = var.project_id
  region      = var.region
  name_prefix = local.name_prefix
}

module "registry" {
  source = "./modules/registry"
  # One repo per environment, so a development teardown deletes its own images only.
  repository_id = local.name_prefix
  project_id    = var.project_id
  region        = var.region
  labels        = var.labels
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
  db_instance_suffix         = var.db_instance_suffix
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
  network_id            = module.network.network_id
  subnet_id             = module.network.subnet_id
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
  network_id            = module.network.network_id
  subnet_id             = module.network.subnet_id
  min_instances         = var.api_min_instances
  max_instances         = var.api_max_instances
  plain_env             = local.plain_env
  secret_env            = local.secret_env

  depends_on = [module.secrets, module.data, module.cloud_run_consumers]
}

module "front_site" {
  source      = "./modules/static_site"
  project_id  = var.project_id
  name_prefix = "${local.name_prefix}-front"
  bucket_name = var.front_bucket_name
  location    = var.bucket_location
  # The CSP reproduces the retired nginx "frame-ancestors *" so regulated operators can still embed.
  # X-Robots-Tag is what actually de-indexes the app: public/robots.txt stops crawling, but Disallow
  # alone never REMOVES a URL Google already knows — and blocking the crawl also stops Google ever
  # reading a noindex meta tag. It covers verify.html and embed-example.html too. The lp bucket
  # deliberately does NOT get this header — the landing page must stay indexable.
  custom_response_headers = [
    "Content-Security-Policy: frame-ancestors *",
    "X-Robots-Tag: noindex",
  ]
  labels = var.labels
}

module "lp_site" {
  source      = "./modules/static_site"
  project_id  = var.project_id
  name_prefix = "${local.name_prefix}-lp"
  bucket_name = var.lp_bucket_name
  location    = var.bucket_location
  # The lp is a one-pager with anchor-only nav — a 404 status is correct, so this is a real error
  # page rather than an SPA fallback. monkify-lp/src/pages/404.astro already builds to dist/404.html.
  not_found_page = "404.html"
  labels         = var.labels
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
