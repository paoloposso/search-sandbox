package main

import (
	"log"
	"os"
	"time"

	"github.com/joho/godotenv"
)

func main() {
	_ = godotenv.Load() // Ignore error if .env doesn't exist

	connStr := os.Getenv("DB_CONNECTION_STRING")
	if connStr == "" {
		connStr = "Host=localhost;Database=movies_db;Username=postgres;Password=postgres_password;sslmode=disable"
	}

	openSearchUrl := os.Getenv("OPENSEARCH_URL")
	if openSearchUrl == "" {
		openSearchUrl = "http://localhost:9200"
	}

	vespaUrl := os.Getenv("VESPA_URL")
	if vespaUrl == "" {
		vespaUrl = "http://localhost:8080/"
	}

	log.Println("Starting Go Feeder...")
	start := time.Now()

	log.Println("Fetching movies from PostgreSQL...")
	movies, err := fetchAllMovies(connStr)
	if err != nil {
		log.Fatalf("Failed to fetch movies: %v", err)
	}

	if len(movies) == 0 {
		log.Println("No movies found to sync. Exiting.")
		return
	}

	log.Printf("Successfully fetched %d movies. Starting synchronization...", len(movies))

	jobs := make(chan MovieSearchDocument, len(movies))
	
	pool := NewWorkerPool(openSearchUrl, vespaUrl, len(movies))
	wg := pool.StartWorkers(20, jobs)

	for _, m := range movies {
		jobs <- m
	}
	close(jobs)
	
	wg.Wait()
	
	log.Printf("Synchronization complete! Took %s", time.Since(start))
}
