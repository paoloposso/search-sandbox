package main

import (
	"database/sql"
	"fmt"
	"log"

	"github.com/lib/pq"
)

func fetchAllMovies(connStr string) ([]MovieSearchDocument, error) {
	db, err := sql.Open("postgres", connStr)
	if err != nil {
		return nil, fmt.Errorf("failed to open database: %w", err)
	}
	defer db.Close()

	if err := db.Ping(); err != nil {
		return nil, fmt.Errorf("failed to ping database: %w", err)
	}

	query := `
SELECT 
    m."Id", 
    m."Title", 
    m."Plot", 
    m."ReleaseYear", 
    m."Rating", 
    d."Name" as DirectorName,
    (SELECT array_agg(a."Name") FROM "Actors" a JOIN "MovieActors" ma ON a."Id" = ma."ActorId" WHERE ma."MovieId" = m."Id") as ActorNames,
    (SELECT array_agg(g."Name") FROM "Genres" g JOIN "GenreMovie" gm ON g."Id" = gm."GenresId" WHERE gm."MoviesId" = m."Id") as Genres
FROM "Movies" m
JOIN "Directors" d ON m."DirectorId" = d."Id"
`
	rows, err := db.Query(query)
	if err != nil {
		return nil, fmt.Errorf("failed to execute query: %w", err)
	}
	defer rows.Close()

	var movies []MovieSearchDocument
	for rows.Next() {
		var m MovieSearchDocument
		var actorNames pq.StringArray
		var genres pq.StringArray
		
		err := rows.Scan(
			&m.Id, &m.Title, &m.Plot, &m.ReleaseYear, &m.Rating, &m.DirectorName, &actorNames, &genres,
		)
		if err != nil {
			log.Printf("failed to scan row: %v", err)
			continue
		}
		
		if actorNames != nil {
			m.ActorNames = actorNames
		} else {
			m.ActorNames = []string{}
		}

		if genres != nil {
			m.Genres = genres
		} else {
			m.Genres = []string{}
		}

		movies = append(movies, m)
	}
	
	if err := rows.Err(); err != nil {
		return nil, fmt.Errorf("row iteration error: %w", err)
	}
	
	return movies, nil
}
