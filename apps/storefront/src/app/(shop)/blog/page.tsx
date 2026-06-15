"use client";

import Link from "next/link";
import Image from "next/image";
import { Calendar, User, ArrowRight } from "lucide-react";
import { Card, Badge, Button, formatDate } from "@overhaul/ui";
import { trpc } from "@/lib/trpc";

export default function BlogPage() {
  const { data } = trpc.blog.list.useQuery({ limit: 12 });

  return (
    <div className="container mx-auto px-4 py-8">
      <div className="text-center mb-12">
        <Badge variant="secondary" className="mb-4">Blog</Badge>
        <h1 className="text-3xl md:text-4xl font-bold mb-4">
          Tips, Finds & Stories
        </h1>
        <p className="text-muted-foreground max-w-xl mx-auto">
          Sourcing tips, repair guides, sustainability stories,
          and behind-the-scenes looks at the secondhand market.
        </p>
      </div>

      {data?.items.length === 0 ? (
        <div className="text-center py-12 text-muted-foreground">
          <p>No blog posts yet. Check back soon!</p>
        </div>
      ) : (
        <div className="grid md:grid-cols-2 lg:grid-cols-3 gap-6">
          {data?.items.map((post) => (
            <Link key={post.id} href={`/blog/${post.slug}`}>
              <Card className="overflow-hidden hover:shadow-md transition-shadow group">
                <div className="aspect-video bg-muted relative">
                  {post.coverImage ? (
                    <Image src={post.coverImage} alt={post.title} fill className="object-cover" />
                  ) : (
                    <div className="flex items-center justify-center h-full text-muted-foreground/20 text-4xl">
                      📝
                    </div>
                  )}
                </div>
                <div className="p-4">
                  <div className="flex gap-1 mb-2">
                    {post.tags.slice(0, 2).map((tag) => (
                      <Badge key={tag} variant="outline" className="text-xs">{tag}</Badge>
                    ))}
                  </div>
                  <h2 className="font-semibold line-clamp-2 group-hover:text-brand-600 transition-colors">
                    {post.title}
                  </h2>
                  {post.excerpt && (
                    <p className="text-sm text-muted-foreground mt-2 line-clamp-2">{post.excerpt}</p>
                  )}
                  <div className="flex items-center gap-4 mt-3 text-xs text-muted-foreground">
                    <span className="flex items-center gap-1">
                      <User className="h-3 w-3" /> {post.authorName}
                    </span>
                    {post.publishedAt && (
                      <span className="flex items-center gap-1">
                        <Calendar className="h-3 w-3" /> {formatDate(post.publishedAt)}
                      </span>
                    )}
                  </div>
                </div>
              </Card>
            </Link>
          ))}
        </div>
      )}
    </div>
  );
}
