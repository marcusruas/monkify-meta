# Private VPC: no public IPs on the data tier; Cloud Run reaches it via a Serverless
# VPC Access connector; egress to the internet (Solana RPC, FreeCurrencyAPI) goes
# through Cloud NAT. Private Service Access peers Cloud SQL + Memorystore.

resource "google_compute_network" "vpc" {
  name                    = "${var.name_prefix}-vpc"
  project                 = var.project_id
  auto_create_subnetworks = false
}

resource "google_compute_subnetwork" "subnet" {
  name                     = "${var.name_prefix}-subnet"
  project                  = var.project_id
  region                   = var.region
  network                  = google_compute_network.vpc.id
  ip_cidr_range            = var.subnet_cidr
  private_ip_google_access = true
}

# Cloud Run reaches the private VPC via DIRECT VPC EGRESS (the Cloud Run v2 service attaches a
# network interface straight to the subnet below — see the cloud_run_* modules), NOT a Serverless
# VPC Access connector. The connector was removed: its Google-managed e2-micro VMs repeatedly failed
# to get healthy ("Error 13: VPC Access connector failed to get healthy") on this project even with
# open quota, non-overlapping CIDRs, and the documented health-probe firewall rules in place. Direct
# VPC egress has no connector VMs, no health-check machinery, and is Google's recommended approach.
# It needs no extra firewall rules: Cloud Run's own health checks are internal, and egress to the
# private data tier rides the Private Service Access peering (default-allowed egress).

# Cloud NAT for private-instance egress.
resource "google_compute_router" "router" {
  name    = "${var.name_prefix}-router"
  project = var.project_id
  region  = var.region
  network = google_compute_network.vpc.id
}

resource "google_compute_router_nat" "nat" {
  name                               = "${var.name_prefix}-nat"
  project                            = var.project_id
  region                             = var.region
  router                             = google_compute_router.router.name
  nat_ip_allocate_option             = "AUTO_ONLY"
  source_subnetwork_ip_ranges_to_nat = "ALL_SUBNETWORKS_ALL_IP_RANGES"
}

# Private Service Access range + peering for Cloud SQL / Memorystore private IPs.
resource "google_compute_global_address" "private_ip_alloc" {
  name          = "${var.name_prefix}-psa-range"
  project       = var.project_id
  purpose       = "VPC_PEERING"
  address_type  = "INTERNAL"
  prefix_length = 16
  network       = google_compute_network.vpc.id
}

resource "google_service_networking_connection" "private_vpc_connection" {
  network                 = google_compute_network.vpc.id
  service                 = "servicenetworking.googleapis.com"
  reserved_peering_ranges = [google_compute_global_address.private_ip_alloc.name]
}
