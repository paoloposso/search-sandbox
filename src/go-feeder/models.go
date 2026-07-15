package main

type MovieSearchDocument struct {
	Id           int      `json:"id"`
	Title        string   `json:"title"`
	Plot         string   `json:"plot"`
	ReleaseYear  int      `json:"release_year"`
	Rating       float64  `json:"rating"`
	DirectorName string   `json:"director_name"`
	Genres       []string `json:"genres"`
	ActorNames   []string `json:"actor_names"`
}

type VespaPayload struct {
	Fields MovieSearchDocument `json:"fields"`
}
