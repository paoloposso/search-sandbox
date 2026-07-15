package main

import (
	"bytes"
	"encoding/json"
	"fmt"
	"io"
	"log"
	"net/http"
	"sync"
	"sync/atomic"
)

type WorkerPool struct {
	openSearchUrl string
	vespaUrl      string
	client        *http.Client
	processed     atomic.Uint64
	total         int
}

func NewWorkerPool(openSearchUrl, vespaUrl string, total int) *WorkerPool {
	return &WorkerPool{
		openSearchUrl: openSearchUrl,
		vespaUrl:      vespaUrl,
		total:         total,
		client: &http.Client{
			Transport: &http.Transport{
				MaxIdleConns:        100,
				MaxIdleConnsPerHost: 100,
			},
		},
	}
}

func (p *WorkerPool) StartWorkers(numWorkers int, jobs <-chan MovieSearchDocument) *sync.WaitGroup {
	var wg sync.WaitGroup
	for i := 0; i < numWorkers; i++ {
		wg.Add(1)
		go func(workerId int) {
			defer wg.Done()
			for movie := range jobs {
				p.processMovie(movie)
			}
		}(i)
	}
	return &wg
}

func (p *WorkerPool) processMovie(movie MovieSearchDocument) {
	var wg sync.WaitGroup
	wg.Add(2)

	go func() {
		defer wg.Done()
		err := p.sendToOpenSearch(movie)
		if err != nil {
			log.Printf("OpenSearch error for movie %d: %v", movie.Id, err)
		}
	}()

	go func() {
		defer wg.Done()
		err := p.sendToVespa(movie)
		if err != nil {
			log.Printf("Vespa error for movie %d: %v", movie.Id, err)
		}
	}()

	wg.Wait()

	count := p.processed.Add(1)
	if count%100 == 0 || int(count) == p.total {
		log.Printf("Progress: %d / %d records synced to both engines.", count, p.total)
	}
}

func (p *WorkerPool) sendToOpenSearch(movie MovieSearchDocument) error {
	url := fmt.Sprintf("%s/movies/_doc/%d", p.openSearchUrl, movie.Id)
	body, err := json.Marshal(movie)
	if err != nil {
		return err
	}

	req, err := http.NewRequest(http.MethodPut, url, bytes.NewReader(body))
	if err != nil {
		return err
	}
	req.Header.Set("Content-Type", "application/json")

	resp, err := p.client.Do(req)
	if err != nil {
		return err
	}
	defer resp.Body.Close()

	// Read full body to ensure connection can be reused
	io.Copy(io.Discard, resp.Body)

	if resp.StatusCode >= 300 {
		return fmt.Errorf("received status %d from OpenSearch", resp.StatusCode)
	}
	return nil
}

func (p *WorkerPool) sendToVespa(movie MovieSearchDocument) error {
	url := fmt.Sprintf("%sdocument/v1/movie/movie/docid/%d", p.vespaUrl, movie.Id)

	payload := VespaPayload{Fields: movie}
	body, err := json.Marshal(payload)
	if err != nil {
		return err
	}

	req, err := http.NewRequest(http.MethodPost, url, bytes.NewReader(body))
	if err != nil {
		return err
	}
	req.Header.Set("Content-Type", "application/json")

	resp, err := p.client.Do(req)
	if err != nil {
		return err
	}
	defer resp.Body.Close()
	io.Copy(io.Discard, resp.Body)

	if resp.StatusCode >= 300 {
		return fmt.Errorf("received status %d from Vespa", resp.StatusCode)
	}
	return nil
}
