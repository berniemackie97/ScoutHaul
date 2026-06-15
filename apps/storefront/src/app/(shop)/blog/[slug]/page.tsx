"use client";

import { useParams } from "next/navigation";
import Link from "next/link";
import { ArrowLeft, Calendar, User, Tag, Clock } from "lucide-react";
import { Badge, Separator } from "@overhaul/ui";
import { trpc } from "@/lib/trpc";

export default function BlogPostPage() {
  const { slug } = useParams<{ slug: string }>();
  const { data: post, isLoading } = trpc.blog.bySlug.useQuery({ slug });

  if (isLoading) {
    return (
      <div className="container mx-auto px-4 py-8 max-w-3xl">
        <div className="animate-pulse space-y-4">
          <div className="h-8 bg-muted rounded w-3/4" />
          <div className="h-4 bg-muted rounded w-1/4" />
          <div className="h-64 bg-muted rounded" />
          <div className="space-y-2">
            <div className="h-4 bg-muted rounded" />
            <div className="h-4 bg-muted rounded w-5/6" />
            <div className="h-4 bg-muted rounded w-4/6" />
          </div>
        </div>
      </div>
    );
  }

  if (!post) {
    return (
      <div className="container mx-auto px-4 py-16 text-center">
        <h1 className="text-2xl font-bold mb-4">Post not found</h1>
        <Link href="/blog" className="text-brand-600 hover:underline">
          Back to blog
        </Link>
      </div>
    );
  }

  const readingTime = Math.max(1, Math.ceil(post.content.split(/\s+/).length / 200));

  return (
    <article className="container mx-auto px-4 py-8 max-w-3xl">
      <Link href="/blog" className="inline-flex items-center gap-2 text-sm text-muted-foreground hover:text-foreground mb-6">
        <ArrowLeft className="h-4 w-4" /> Back to blog
      </Link>

      {post.coverImage && (
        <div className="aspect-video rounded-xl overflow-hidden mb-8 bg-muted">
          <img src={post.coverImage} alt={post.title} className="w-full h-full object-cover" />
        </div>
      )}

      <div className="flex flex-wrap gap-2 mb-4">
        {post.tags.map((tag) => (
          <Badge key={tag} variant="secondary">
            <Tag className="h-3 w-3 mr-1" /> {tag}
          </Badge>
        ))}
      </div>

      <h1 className="text-4xl font-bold mb-4">{post.title}</h1>

      <div className="flex items-center gap-4 text-sm text-muted-foreground mb-8">
        <span className="flex items-center gap-1">
          <User className="h-4 w-4" /> {post.author.firstName} {post.author.lastName}
        </span>
        <span className="flex items-center gap-1">
          <Calendar className="h-4 w-4" /> {new Date(post.publishedAt ?? post.createdAt).toLocaleDateString()}
        </span>
        <span className="flex items-center gap-1">
          <Clock className="h-4 w-4" /> {readingTime} min read
        </span>
      </div>

      <Separator className="mb-8" />

      <div className="prose prose-lg max-w-none">
        {post.content.split("\n").map((p, i) => (
          <p key={i} className="mb-4 text-foreground/90 leading-relaxed">{p}</p>
        ))}
      </div>
    </article>
  );
}
